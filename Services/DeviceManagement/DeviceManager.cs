using Ape.Core.Logging;
using Ape.Core.Scene;
using Ape.Core.Runtime.Service;
using Ape.Module.DeviceManager.SceneEntities;
using Ape.Module.DeviceManager.SceneEntities.Models;
using Ape.Module.DeviceManager.Services.DeviceManagement.Serial;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;


namespace Ape.Module.DeviceManager.Services.DeviceManagement;

/// <summary>
/// Core device manager - handles detection, filtering, and subscription callbacks.
/// Provides API for subscribing to device events with custom filters and handlers.
/// 
/// Features:
///   - Cross-platform device detection (HID, Serial, USB)
///   - Subscription-based device discovery with filters
///   - Automatic DeviceNode creation in SceneGraph
///   - Data handler support for reading/writing device data
///   - Hotplug detection via polling
/// 
/// Example usage:
///   var deviceManager = new DeviceManager(sceneManager, logger);
///   deviceManager.Start(pollIntervalMs: 2000);
///   
///   deviceManager.Subscribe(
///       DeviceFilter.ByVendorId(0x2341), // Arduino devices
///       devices => {
///           foreach (var device in devices) {
///               var handler = deviceManager.GetDataHandler(device);
///               handler.DataReceived += (s, data) => {
///                   device.DeviceData["temperature"] = ParseTemperature(data);
///               };
///               await handler.OpenAsync();
///           }
///       }
///   );
/// </summary>
public class DeviceManager : IDeviceManager
{
    private readonly ISceneManager _sceneManager;
    private readonly ILogger _logger;
    
    private readonly Dictionary<string, DeviceInternal> _devices = new(); // DevicePath → DeviceInternal
    private readonly Dictionary<string, SerialDataHandler> _serialHandlers = new();
    private readonly Dictionary<Guid, Subscription> _subscriptions = new(); // SubscriptionId → Subscription
    private readonly ISerialPortEnumerator _serialPortEnumerator;
    private readonly object _lock = new();

    private Task? _scanTask;
    private CancellationTokenSource? _cts;
    private int _pollInterval = 2000;

    public DeviceManager(ISceneManager sceneManager, ILogger logger, ISerialPortEnumerator? serialPortEnumerator = null)
    {
        _sceneManager = sceneManager;
        _logger = logger;
        _serialPortEnumerator = serialPortEnumerator ?? SerialPortEnumerator.CreateDefault();
    }

    /// <summary>
    /// Subscribe to device events with filter and callback.
    /// The callback is invoked immediately for existing matching devices,
    /// and again whenever new matching devices are detected.
    /// </summary>
    /// <param name="filter">Device filter predicate (e.g., ByVendorId, ByName, etc.)</param>
    /// <param name="onDevicesFound">Callback invoked with list of matching devices</param>
    /// <returns>Subscription ID (use for unsubscribing later)</returns>
    public Guid Subscribe(DeviceFilter filter, Action<List<IDevice>> onDevicesFound)
    {
        var subscriptionId = Guid.NewGuid();
        var subscription = new Subscription
        {
            Id = subscriptionId,
            Filter = filter,
            Callback = onDevicesFound
        };

        lock (_lock)
        {
            _subscriptions[subscriptionId] = subscription;
        }

        _logger.LogInfo($"[DeviceManager] New subscription: {subscriptionId}");

        // Immediately check existing devices
        CheckSubscription(subscription);
        
        // Trigger a new scan to check if any previously ignored devices now match
        if (_scanTask != null) // Only if scanning is already running
        {
            Task.Run(() => ScanDevicesAsync());
        }

        return subscriptionId;
    }

    /// <summary>
    /// Unsubscribe from device events.
    /// </summary>
    /// <param name="subscriptionId">Subscription ID returned by Subscribe()</param>
    public void Unsubscribe(Guid subscriptionId)
    {
        lock (_lock)
        {
            _subscriptions.Remove(subscriptionId);
        }
        _logger.LogInfo($"[DeviceManager] Unsubscribed: {subscriptionId}");
    }

    /// <summary>
    /// Start device monitoring loop.
    /// Scans for devices at the specified poll interval.
    /// </summary>
    /// <param name="pollIntervalMs">Polling interval in milliseconds (default: 2000ms)</param>
    public void Start(int pollIntervalMs = 2000)
    {
        if (_scanTask != null)
        {
            _logger.LogWarning("[DeviceManager] Already started");
            return;
        }

        _pollInterval = pollIntervalMs;
        _cts = new CancellationTokenSource();
        _scanTask = Task.Run(() => ScanLoopAsync(_cts.Token));
        _logger.LogInfo($"[DeviceManager] Started (poll interval: {_pollInterval}ms)");
    }

    /// <summary>
    /// Stop device monitoring.
    /// </summary>
    public void Stop()
    {
        if (_cts == null || _scanTask == null)
        {
            return;
        }

        _cts.Cancel();
        _scanTask.Wait(TimeSpan.FromSeconds(2));
        _cts.Dispose();
        _cts = null;
        _scanTask = null;
        _logger.LogInfo("[DeviceManager] Stopped");
    }

    /// <summary>
    /// Open a serial port by path with explicit line settings (bypasses device discovery).
    /// </summary>
    public IDeviceDataHandler OpenSerial(string devicePath, SerialPortOptions? options = null)
    {
        var opts = options ?? new SerialPortOptions();
        var cacheKey = $"{devicePath}|{opts.BaudRate}|{opts.Parity}|{opts.DataBits}|{(int)opts.StopBits}";

        lock (_lock)
        {
            if (_serialHandlers.TryGetValue(cacheKey, out var existing))
            {
                return existing;
            }

            var handler = new SerialDataHandler(devicePath, _logger, opts);
            _serialHandlers[cacheKey] = handler;
            return handler;
        }
    }

    /// <summary>
    /// Get data handler for reading/writing device data.
    /// Returns SerialDataHandler for serial devices, HidDataHandler for HID devices.
    /// </summary>
    /// <param name="device">DeviceNode to create handler for</param>
    /// <returns>Device data handler, or null if device type not supported</returns>
    public IDeviceDataHandler? GetDataHandler(IDevice device)
    {
        if (device.DeviceType == DeviceType.Serial)
        {
            return new SerialDataHandler(device.DevicePath, _logger);
        }
        else if (device.DeviceType == DeviceType.HID)
        {
            return new HidDataHandler(device.DevicePath, _logger);
        }

        _logger.LogWarning($"[DeviceManager] No data handler available for device type: {device.DeviceType}");
        return null;
    }

    /// <inheritdoc />
    public List<IDevice> QuerySerialPorts(DeviceFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        return _serialPortEnumerator.Enumerate()
            .Select(SerialPortDeviceFactory.ToDeviceInternal)
            .Cast<IDevice>()
            .Where(filter.Predicate)
            .ToList();
    }

    /// <summary>
    /// Get all currently detected devices.
    /// </summary>
    public List<IDevice> GetAllDevices()
    {
        lock (_lock)
        {
            return _devices.Values.Cast<IDevice>().ToList();
        }
    }

    /// <summary>
    /// Get device by path.
    /// </summary>
    public IDevice? GetDeviceByPath(string devicePath)
    {
        lock (_lock)
        {
            _devices.TryGetValue(devicePath, out var device);
            return device;
        }
    }

    private async Task ScanLoopAsync(CancellationToken ct)
    {
        // Initial scan
        await ScanDevicesAsync();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_pollInterval, ct);
                await ScanDevicesAsync();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[DeviceManager] Scan error: {ex.Message}");
            }
        }
    }

    private async Task ScanDevicesAsync()
    {
        var currentDevices = new HashSet<string>();
        var newDevicesCreated = false;

        // Scan HID devices
        try
        {
            var hidDevices = HidSharp.DeviceList.Local.GetHidDevices();
            foreach (var device in hidDevices)
            {
                var devicePath = device.DevicePath;
                currentDevices.Add(devicePath);

                lock (_lock)
                {
                    if (!_devices.ContainsKey(devicePath))
                    {
                        // Skip if no active subscriptions
                        if (_subscriptions.Count == 0)
                            continue;

                        // Get serial number (some devices don't have one)
                        string serialNumber = string.Empty;
                        try
                        {
                            serialNumber = device.GetSerialNumber() ?? string.Empty;
                        }
                        catch { }

                        // Get friendly name
                        string friendlyName;
                        try
                        {
                            friendlyName = device.GetFriendlyName() ?? $"HID Device {device.VendorID:X4}:{device.ProductID:X4}";
                        }
                        catch
                        {
                            friendlyName = $"HID Device {device.VendorID:X4}:{device.ProductID:X4}";
                        }

                        // Create lightweight DeviceInternal for filter testing (no Replica overhead!)
                        var deviceNodeInternal = new DeviceInternal
                        {
                            Id = $"HID_{device.ProductID:X4}_{serialNumber}",
                            DeviceType = DeviceType.HID,
                            DevicePath = devicePath,
                            FriendlyName = friendlyName,
                            VendorId = device.VendorID,
                            ProductId = device.ProductID,
                            SerialNumber = serialNumber,
                            Connected = true,
                            LastUpdate = DateTime.UtcNow
                        };
                        
                        // Test filter predicates on the lightweight object
                        bool matchesAnyFilter = _subscriptions.Values.Any(sub => sub.Filter.Predicate(deviceNodeInternal));
                        
                        if (matchesAnyFilter)
                        {
                            // Store the DeviceInternal (lightweight, no Replica overhead)
                            // Plugins can create Device (Replica) if they need network sync
                            _devices[devicePath] = deviceNodeInternal;
                            _logger.LogInfo($"[DeviceManager] 🔌 HID connected: {friendlyName}");
                            newDevicesCreated = true;
                        }
                        // else: Device doesn't match any filter - skip creation entirely (no log spam!)
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"[DeviceManager] HID scan error: {ex.Message}");
        }

        // Scan Serial ports (USB metadata when available)
        try
        {
            foreach (var portInfo in _serialPortEnumerator.Enumerate())
            {
                var portName = portInfo.CalloutPath;
                currentDevices.Add(portName);

                lock (_lock)
                {
                    if (!_devices.ContainsKey(portName))
                    {
                        // Skip if no active subscriptions
                        if (_subscriptions.Count == 0)
                            continue;

                        var deviceNodeInternal = SerialPortDeviceFactory.ToDeviceInternal(portInfo);

                        bool matchesAnyFilter = _subscriptions.Values.Any(sub => sub.Filter.Predicate(deviceNodeInternal));

                        if (matchesAnyFilter)
                        {
                            _devices[portName] = deviceNodeInternal;
                            _logger.LogInfo($"[DeviceManager] 🔌 Serial connected: {deviceNodeInternal.FriendlyName} @ {portName}");
                            newDevicesCreated = true;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"[DeviceManager] Serial scan error: {ex.Message}");
        }

        // Handle disconnections
        lock (_lock)
        {
            var disconnectedPaths = _devices.Keys.Where(p => !currentDevices.Contains(p)).ToList();
            foreach (var path in disconnectedPaths)
            {
                if (_devices.TryGetValue(path, out var deviceNode))
                {
                    // Thread-safe: Lock DeviceNode while modifying properties
                    lock (deviceNode.SyncRoot)
                    {
                        deviceNode.Connected = false;
                        deviceNode.LastUpdate = DateTime.UtcNow;
                    }
                    _logger.LogInfo($"[DeviceManager] 🔌 Disconnected: {deviceNode.FriendlyName}");
                    
                    // TODO: Optionally remove from _devices dictionary after some timeout
                    // For now, we keep disconnected devices in the list
                }
            }
        }

        // Only check subscriptions if we created new devices
        if (newDevicesCreated)
        {
            CheckAllSubscriptions();
        }

        await Task.CompletedTask;
    }

    private void CheckAllSubscriptions()
    {
        lock (_lock)
        {
            foreach (var sub in _subscriptions.Values)
            {
                CheckSubscription(sub);
            }
        }
    }

    private void CheckSubscription(Subscription subscription)
    {
        lock (_lock)
        {
            // Thread-safe: We hold _lock while reading device properties for filtering
            // Plugins receiving the device list MUST use device.WithLock() to read properties safely!
            var matchingDevices = _devices.Values
                .Where(d => d.Connected && subscription.Filter.Predicate(d))
                .Cast<IDevice>()
                .ToList();

            if (matchingDevices.Any())
            {
                // Invoke callback in background thread to avoid blocking
                Task.Run(() =>
                {
                    try
                    {
                        subscription.Callback(matchingDevices);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"[DeviceManager] Subscription callback error: {ex.Message}");
                    }
                });
            }
        }
    }

    private class Subscription
    {
        public Guid Id { get; set; }
        public DeviceFilter Filter { get; set; } = null!;
        public Action<List<IDevice>> Callback { get; set; } = null!;
    }
}
