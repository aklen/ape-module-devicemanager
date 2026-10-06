using Ape.Core.Logging;
using Ape.Core.Scene;
using Ape.Module.DeviceManager;
using Ape.Core.Network;
using Ape.Core.Runtime.Plugin;
using Ape.Core.Runtime.Service;
using Ape.Core.Event;
using Ape.Core.Replication;
using Ape.Module.DeviceManager.SceneEntities;
using Ape.Module.DeviceManager.SceneEntities.Models;
using Ape.Module.DeviceManager.Services.DeviceManagement;
using Ape.Core.Utils;
using System;
using System.Threading;

namespace Ape.Module.DeviceManager.Plugin.DeviceExample;

/// <summary>
/// Example plugin demonstrating IDeviceManager usage with Device (Replica) synchronization.
/// Lives under <c>Ape.Module.DeviceManager/Plugins/DeviceExample</c>.
/// Shows how to:
///   1. Subscribe to devices via IDeviceManager (lightweight detection)
///   2. Create synchronized Device replicas via ISceneManager.CreateRegisteredEntity
///   3. Implement ownership pattern: owner reads/writes hardware, remote peers read-only
///
/// Prerequisites:
///   - Enable <c>Ape.Module.DeviceManager</c> in config so <c>ApeSystem</c> loads <c>DeviceManagerService</c>. For testing, create virtual serial ports with socat:
///     socat -d -d pty,raw,echo=0 pty,raw,echo=0
///     (Example output: /dev/ttys082 and /dev/ttys083 linked)
///
/// Example config.json (see also docs/ARCHITECTURE.md — Modular JSON config):
///   {
///     "services": [],
///     "plugins": [],
///     "modules": {
///       "Ape.Core.Network": { "enabled": true, "transport": "quic", "role": "server", "port": 9050 },
///       "Ape.Module.DeviceManager": {
///         "services": { "DeviceManagerService": {} },
///         "plugins": { "DeviceExample": {} }
///       }
///     }
///   }
///
/// Usage:
///   1. Build: ./ape build
///   2. Run: ./ape run src/Ape.Modules/Ape.Module.DeviceManager/Samples/device-example.json
///   3. Send data: echo "Hello from device!" > /dev/ttys083
///   4. See output in console with ASCII+HEX formatting
///   5. Device metadata and data automatically sync across network to remote peers
/// </summary>
public class DeviceExamplePlugin : IPlugin
{
    public string PluginId => "device-example";
    public string Name => "Device Example Plugin";

    private IDeviceManager? _deviceManager;
    private IEventManager? _eventManager;
    private ISceneManager? _sceneManager;
    private INetworkManager? _networkManager;
    private ILogger? _logger;

    public void OnInit(IServiceProvider services)
    {
        _logger = services.GetService(typeof(ILogger)) as ILogger;
        _deviceManager = services.GetService(typeof(IDeviceManager)) as IDeviceManager;
        _eventManager = services.GetService(typeof(IEventManager)) as IEventManager;
        _sceneManager = services.GetService(typeof(ISceneManager)) as ISceneManager;
        _networkManager = services.GetService(typeof(INetworkManager)) as INetworkManager;

        if (_deviceManager == null)
        {
            _logger?.LogWarning($"[{Name}] ⚠️ IDeviceManager not available.");
            _logger?.LogWarning($"[{Name}]    Expected DeviceManagerService from the Device module DLL; check config modules and ApeSystem startup.");
            return;
        }

        if (_sceneManager == null)
            throw PluginDirectSceneWrite.Unavailable(Name);

        if (_networkManager == null)
        {
            _logger?.LogWarning($"[{Name}] ⚠️ INetworkManager not available.");
            return;
        }

        _eventManager?.Subscribe<PropertyChangedEvent>(PluginId, OnPropertyChanged);

        _logger?.LogInfo($"[{Name}] ✅ IDeviceManager, ISceneManager, INetworkManager acquired!");
        _logger?.LogInfo($"[{Name}] Subscribing to virtual serial ports (/dev/ttys077)...");
        _logger?.LogInfo($"[{Name}] Local Peer ID: {_networkManager.LocalPeerId}");

        // ============================================================================
        // DEVICE REPLICATION PATTERN:
        // ============================================================================
        // 1. IDeviceManager detects hardware (lightweight DeviceInternal)
        // 2. Plugin creates Device replica via CreateRegisteredEntity(DeviceSceneEntityTypeIds.Device, ...)
        // 3. Device metadata + data automatically sync across network (via Replica)
        //
        // OWNERSHIP PATTERN:
        // - Owner (device.OwnerId == localPeerId):
        //     * CAN read/write hardware directly (Serial, GPIO, HID)
        //     * Updates DeviceData with sensor readings
        //     * Changes automatically sync to remote peers
        //
        // - Remote peers (device.OwnerId != localPeerId):
        //     * CANNOT access hardware (no GetDataHandler)
        //     * CAN read DeviceData (temperature, humidity, etc.)
        //     * CAN read metadata (VendorId, ProductId, DevicePath, etc.)
        //     * Receive automatic updates when owner writes DeviceData
        // ============================================================================

        _logger?.LogInfo($"[{Name}] Network Role: {_networkManager.Role}");
        if (_networkManager.Role == "server")
        {
            // Example: Subscribe to specific virtual serial port and create synchronized Device replica
            // Change this path to match your socat output!
            _deviceManager.Subscribe(
                DeviceFilter.ByType(DeviceType.Serial)
                    // .And(DeviceFilter.ByPath("/dev/ttys077")),
                    .And(DeviceFilter.ByPath("/dev/cu.usbserial-A50285BI")),
                devices =>
                {
                    _logger?.LogInfo($"[{Name}] 🔌 Detected {devices.Count} matching device(s)");

                    foreach (var detectedDevice in devices)
                    {
                        _logger?.LogInfo($"[{Name}]    - {detectedDevice.FriendlyName} @ {detectedDevice.DevicePath}");

                        // Create synchronized Device replica via SceneManager
                        // This Device will automatically replicate across the network!
                        var deviceReplica = (IDevice)_sceneManager.CreateRegisteredEntity(
                            DeviceSceneEntityTypeIds.Device,
                            name: detectedDevice.DevicePath,
                            ownerId: _networkManager.LocalPeerId
                        );

                        // Set device metadata (will sync across network)
                        deviceReplica.DeviceType = detectedDevice.DeviceType;
                        deviceReplica.DevicePath = detectedDevice.DevicePath;
                        deviceReplica.FriendlyName = detectedDevice.FriendlyName;
                        deviceReplica.Connected = true;
                        deviceReplica.LastUpdate = DateTime.UtcNow;

                        _logger?.LogInfo($"[{Name}]    ✅ Created Device replica: {deviceReplica.Id} (Owner: {deviceReplica.OwnerId})");

                        // Only the owner should access hardware directly!
                        // if (deviceReplica.OwnerId == _networkManager.LocalPeerId)
                        // {
                        _logger?.LogInfo($"[{Name}]    👑 This peer owns the device - setting up hardware access");

                        var handler = _deviceManager.GetDataHandler(detectedDevice);
                        if (handler != null)
                        {
                            handler.DataReceived += (sender, data) =>
                            {
                                // Format data in ASCII + HEX
                                var formattedData = DataFormatter.Format(data, DataDisplayMode.Both);
                                _logger?.LogInfo($"[{Name}] 📡 Received data: {formattedData}");

                                // Thread-safe update - this will automatically sync to remote peers!
                                lock (deviceReplica.SyncRoot)
                                {
                                    // Use extension method to safely append data
                                    deviceReplica.DeviceData.AppendValue("lastData", System.Text.Encoding.UTF8.GetString(data));
                                    deviceReplica.DeviceData.AppendValue("lastDataHex", DataFormatter.Format(data, DataDisplayMode.HexOnly));
                                    deviceReplica.DeviceData.AppendValue("lastDataAscii", DataFormatter.Format(data, DataDisplayMode.AsciiOnly));
                                    deviceReplica.DeviceData["lastDataTime"] = DateTime.UtcNow;
                                    deviceReplica.LastUpdate = DateTime.UtcNow;

                                    // CRITICAL: Dictionary changes don't trigger PropertyChanged automatically
                                    // Must manually notify to sync changes across network!
                                    deviceReplica.NotifyPropertyChanged("DeviceData");
                                    // deviceReplica.SnapshotProperties();
                                }
                            };

                            _ = handler.OpenAsync();
                            _logger?.LogInfo($"[{Name}]    ✅ Data handler opened for {deviceReplica.Id}");

                            Thread.Sleep(5000);
                            handler.WriteAsync(System.Text.Encoding.ASCII.GetBytes("Hello from owner!\n"));
                        }
                        // }
                        // else
                        // {
                        //     // Remote peer - can only read DeviceData, no hardware access
                        //     _logger?.LogInfo($"[{Name}]    👀 Remote device - read-only access to DeviceData");
                        //     _logger?.LogInfo($"[{Name}]       VendorId: {deviceReplica.VendorId}, ProductId: {deviceReplica.ProductId}");
                        // }
                    }
                }
            );

            // Example: Subscribe to Arduino devices (vendor ID 0x2341) with Device replication
            /*

        _deviceManager.Subscribe(
            DeviceFilter.ByVendorId(0x2341),
            devices =>
            {
                _logger?.LogInfo($"[{Name}] 🔌 Found {devices.Count} Arduino device(s)!");
                foreach (var detectedDevice in devices)
                {
                    _logger?.LogInfo($"[{Name}]    - {detectedDevice.FriendlyName} (VID: {detectedDevice.VendorId:X4}, PID: {detectedDevice.ProductId:X4})");

                    // Create synchronized Device replica
                    var arduinoReplica = (IDevice)_sceneManager.CreateRegisteredEntity(
                        DeviceSceneEntityTypeIds.Device,
                        name: $"Arduino_{detectedDevice.VendorId:X4}_{detectedDevice.ProductId:X4}",
                        ownerId: _networkManager.LocalPeerId
                    );

                    arduinoReplica.DeviceType = detectedDevice.DeviceType;
                    arduinoReplica.VendorId = detectedDevice.VendorId;
                    arduinoReplica.ProductId = detectedDevice.ProductId;
                    arduinoReplica.DevicePath = detectedDevice.DevicePath;
                    arduinoReplica.FriendlyName = detectedDevice.FriendlyName;
                    arduinoReplica.Connected = true;

                    // Only owner accesses hardware
                    if (arduinoReplica.OwnerId == _networkManager.LocalPeerId)
                    {
                        var handler = _deviceManager.GetDataHandler(detectedDevice);
                        if (handler != null)
                        {
                            handler.DataReceived += (s, data) =>
                            {
                                _logger?.LogInfo($"[{Name}] 📊 Arduino data: {BitConverter.ToString(data)}");

                                // Thread-safe update - syncs to remote peers
                                lock (arduinoReplica.SyncRoot)
                                {
                                    // Parse sensor data (example)
                                    arduinoReplica.DeviceData["temperature"] = ParseTemperature(data);
                                    arduinoReplica.DeviceData["humidity"] = ParseHumidity(data);
                                    arduinoReplica.DeviceData["rawData"] = data;
                                    arduinoReplica.LastUpdate = DateTime.UtcNow;
                                }
                            };

                            _ = handler.OpenAsync();
                        }
                    }
                }
            }
        );
        */
        }
        else // client or peer
        {
            _logger?.LogInfo($"[{Name}] ⚠️ Not the server - Device ownership and hardware access disabled.");
        }
    }

    public void OnRun(CancellationToken ct)
    {
        if (_deviceManager == null)
        {
            _logger?.LogWarning($"[{Name}] Running without IDeviceManager (not available)");
        }
        else
        {
            _logger?.LogInfo($"[{Name}] Running... (waiting for device events)");
        }

        // Plugin event loop - drain PropertyChangedEvent queue
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Drain events from our queue - this processes PropertyChangedEvent callbacks!
                _eventManager?.DrainEventsFor(PluginId);

                // Small sleep to prevent CPU spinning
                Thread.Sleep(50);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[{Name}] Error in event loop: {ex.Message}");
                Thread.Sleep(100); // Longer sleep on error
            }
        }

        _logger?.LogInfo($"[{Name}] Event loop stopped.");
    }

    public void OnShutdown()
    {
        _logger?.LogInfo($"[{Name}] Shutdown");
    }

    private void OnPropertyChanged(PropertyChangedEvent evt)
    {
        try
        {
            // Only process PropertyChangedEvent on client side, not on server
            if (_networkManager?.Role != "client")
            {
                return; // Skip processing on server and other roles
            }

            _logger?.LogInfo($"[{Name}] 🔔 PropertyChangedEvent received for ReplicaId: {evt.ReplicaId}, Property: {evt.PropertyName}");
            string subjectName = evt.ReplicaId;
            IDevice? deviceReplica = _sceneManager?.GetEntity<IDevice>(subjectName);
            if (deviceReplica != null)
            {
                _logger?.LogInfo($"[{Name}] 📱 Device changed - ID: {deviceReplica.Id}, Property: {evt.PropertyName}, Connected: {deviceReplica.Connected}");
                if (evt.PropertyName == "DeviceData")
                {
                    if (deviceReplica.DeviceData.TryGetValue("lastDataAscii", out var asciiObj) && asciiObj is string ascii)
                    {
                        _logger?.LogInfo($"[{Name}] 📝 Last data as ASCII: \"{ascii}\"");
                    }
                }
            }
            else
            {
                _logger?.LogWarning($"[{Name}] ⚠️ Device '{subjectName}' not found in SceneManager._devices");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError($"[{Name}] ❌ Error in OnPropertyChanged: {ex.Message}");
            _logger?.LogError($"[{Name}] Exception: {ex}");
        }
    }

    private float ParseTemperature(byte[] data) => 25.5f;
    private float ParseHumidity(byte[] data) => 60.0f;
}
