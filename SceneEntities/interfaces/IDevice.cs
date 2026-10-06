using Ape.Core.Scene;
using Ape.Module.DeviceManager.SceneEntities.Models;

namespace Ape.Module.DeviceManager.SceneEntities;

/// <summary>
/// Interface for hardware device entities.
/// Devices are functional entities (like Light, Geometry) that represent hardware.
/// </summary>
public interface IDevice : IEntity
{
    /// <summary>
    /// Type of device (USB, Serial, HID, Bluetooth, Network).
    /// </summary>
    DeviceType DeviceType { get; set; }

    /// <summary>
    /// USB Vendor ID.
    /// </summary>
    int VendorId { get; set; }

    /// <summary>
    /// USB Product ID.
    /// </summary>
    int ProductId { get; set; }

    /// <summary>
    /// Device serial number.
    /// </summary>
    string SerialNumber { get; set; }

    /// <summary>
    /// Physical device path (e.g., /dev/ttyUSB0, COM3).
    /// </summary>
    string DevicePath { get; set; }

    /// <summary>
    /// Human-readable device name.
    /// </summary>
    string FriendlyName { get; set; }

    /// <summary>
    /// Whether the device is currently connected.
    /// </summary>
    bool Connected { get; set; }

    /// <summary>
    /// Last update timestamp (UTC).
    /// </summary>
    DateTime LastUpdate { get; set; }

    /// <summary>
    /// Device-specific data (sensor readings, button states, etc.).
    /// </summary>
    Dictionary<string, object> DeviceData { get; set; }
}
