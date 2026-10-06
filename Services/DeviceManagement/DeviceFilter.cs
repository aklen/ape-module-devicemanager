using Ape.Module.DeviceManager.SceneEntities;
using Ape.Module.DeviceManager.SceneEntities.Models;

namespace Ape.Module.DeviceManager.Services.DeviceManagement;

/// <summary>
/// Filter predicate builder for device subscription.
/// Provides fluent API for filtering devices by various criteria.
///
/// Example usage:
///   // Single filter
///   var filter = DeviceFilter.ByVendorId(0x2341); // Arduino devices
///
///   // Combined filters
///   var filter = DeviceFilter.ByType(DeviceType.Serial)
///                            .And(DeviceFilter.ByPath("/dev/ttyUSB"));
///
///   // Custom predicate
///   var filter = DeviceFilter.Custom(d => d.VendorId == 0x2341 && d.Connected);
/// </summary>
public class DeviceFilter
{
    public Func<IDevice, bool> Predicate { get; }

    private DeviceFilter(Func<IDevice, bool> predicate)
    {
        Predicate = predicate;
    }

    /// <summary>
    /// Filter devices by ID (case-insensitive substring match)
    /// </summary>
    public static DeviceFilter ById(string id)
        => new(d => d.Id.Contains(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Filter devices by serial number (case-insensitive exact match)
    /// </summary>
    public static DeviceFilter BySerial(string serial)
        => new(d => d.SerialNumber.Equals(serial, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Filter devices by exact device path (case-insensitive).
  /// For Linux <c>/dev/serial/by-id/...</c> symlinks, use <see cref="IDeviceManager.QuerySerialPorts"/> which resolves links.
    /// </summary>
    public static DeviceFilter ByPath(string pathPattern)
        => new(d => d.DevicePath.Equals(pathPattern, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Filter USB serial devices by interface port index (0 = port0), stored in
    /// <see cref="DeviceDataKeys.UsbPortIndex"/>.
    /// </summary>
    public static DeviceFilter ByPortIndex(int portIndex)
        => new(d => d.DeviceData.TryGetValue(DeviceDataKeys.UsbPortIndex, out var value)
                    && value is int index
                    && index == portIndex);

    /// <summary>
    /// Filter devices by USB product name substring (case-insensitive), e.g. "Dual RS232-HS".
    /// </summary>
    public static DeviceFilter ByProductName(string productName)
        => new(d => d.FriendlyName.Contains(productName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Filter devices by USB Vendor ID
    /// Example: ByVendorId(0x2341) for Arduino devices
    /// </summary>
    public static DeviceFilter ByVendorId(int vendorId)
        => new(d => d.VendorId == vendorId);

    /// <summary>
    /// Filter devices by USB Product ID
    /// </summary>
    public static DeviceFilter ByProductId(int productId)
        => new(d => d.ProductId == productId);

    /// <summary>
    /// Filter devices by type (USB, Serial, HID, Bluetooth, Network)
    /// </summary>
    public static DeviceFilter ByType(DeviceType type)
        => new(d => d.DeviceType == type);

    /// <summary>
    /// Filter devices by friendly name (case-insensitive substring match)
    /// Example: ByFriendlyName("Arduino") matches "Arduino Uno", "Arduino Mega", etc.
    /// </summary>
    public static DeviceFilter ByFriendlyName(string friendlyName)
        => new(d => d.FriendlyName.Contains(friendlyName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Filter devices by USB Vendor ID and Product ID combination
    /// </summary>
    public static DeviceFilter ByVendorProduct(int vendorId, int productId)
        => new(d => d.VendorId == vendorId && d.ProductId == productId);

    /// <summary>
    /// Filter only connected devices
    /// </summary>
    public static DeviceFilter OnlyConnected()
        => new(d => d.Connected);

    /// <summary>
    /// Create a custom filter with a lambda predicate
    /// </summary>
    public static DeviceFilter Custom(Func<IDevice, bool> predicate)
        => new(predicate);

    /// <summary>
    /// Combine two filters with AND logic (both must match)
    /// </summary>
    public DeviceFilter And(DeviceFilter other)
        => new(d => Predicate(d) && other.Predicate(d));

    /// <summary>
    /// Combine two filters with OR logic (either can match)
    /// </summary>
    public DeviceFilter Or(DeviceFilter other)
        => new(d => Predicate(d) || other.Predicate(d));

    /// <summary>
    /// Negate the filter (NOT logic)
    /// </summary>
    public DeviceFilter Not()
        => new(d => !Predicate(d));
}
