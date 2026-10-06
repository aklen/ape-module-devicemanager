using Ape.Core.Runtime.Service;
using Ape.Module.DeviceManager.SceneEntities;

namespace Ape.Module.DeviceManager.Services.DeviceManagement;

/// <summary>
/// Device management interface for cross-platform device detection and communication.
/// Provides subscription-based device discovery with filters and data handlers.
///
/// Implementations:
///   - <see cref="DeviceManagerService"/> in this module (HID + Serial); registered by the host as an in-process <see cref="IPluggableService"/>.
///
/// Usage example:
///   var deviceManager = services.GetService&lt;IDeviceManager&gt;();
///   if (deviceManager == null) {
///       logger.LogWarning("IDeviceManager not available");
///       return;
///   }
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
public interface IDeviceManager
{
    /// <summary>
    /// Subscribe to device events with a filter.
    /// Callback is invoked immediately for existing matching devices,
    /// and again whenever new matching devices are detected.
    /// </summary>
    /// <param name="filter">Device filter predicate (e.g., ByVendorId, ByType, ByPath)</param>
    /// <param name="onDevicesFound">Action invoked with list of matching devices</param>
    /// <returns>Subscription ID for later unsubscribing</returns>
    Guid Subscribe(DeviceFilter filter, Action<List<IDevice>> onDevicesFound);

    /// <summary>
    /// Unsubscribe from device events.
    /// </summary>
    /// <param name="subscriptionId">Subscription ID returned by Subscribe()</param>
    void Unsubscribe(Guid subscriptionId);

    /// <summary>
    /// Open a serial port by path with explicit line settings.
    /// The path must match a device named in host config; otherwise this throws.
    /// Returns a cached handler for the same path and options.
    /// </summary>
    IDeviceDataHandler OpenSerial(string devicePath, SerialPortOptions? options = null);

    /// <summary>
    /// Get data handler for reading/writing device data.
    /// Returns SerialDataHandler for serial devices, HidDataHandler for HID devices.
    /// </summary>
    /// <param name="device">Device to create handler for</param>
    /// <returns>Device data handler or null if device type not supported</returns>
    IDeviceDataHandler? GetDataHandler(IDevice device);

    /// <summary>
    /// Enumerate serial ports with USB metadata and return those matching <paramref name="filter"/>
    /// and the device target in host config. Returns an empty list when config names no device.
    /// </summary>
    List<IDevice> QuerySerialPorts(DeviceFilter filter);

    /// <summary>
    /// Get all currently detected devices (both connected and disconnected).
    /// </summary>
    /// <returns>List of all devices in the system</returns>
    List<IDevice> GetAllDevices();

    /// <summary>
    /// Get device by its path.
    /// </summary>
    /// <param name="devicePath">Device path (e.g., "/dev/ttyUSB0", "\\?\hid#...")</param>
    /// <returns>Device if found, null otherwise</returns>
    IDevice? GetDeviceByPath(string devicePath);

    /// <summary>
    /// Start device monitoring with specified polling interval.
    /// Continuously scans for new devices and disconnections.
    /// </summary>
    /// <param name="pollIntervalMs">Polling interval in milliseconds (default: 2000ms)</param>
    void Start(int pollIntervalMs = 2000);

    /// <summary>
    /// Stop device monitoring and cleanup resources.
    /// </summary>
    void Stop();
}
