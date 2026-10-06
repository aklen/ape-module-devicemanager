using Ape.Module.DeviceManager.SceneEntities.Models;

namespace Ape.Module.DeviceManager.Services.DeviceManagement.Serial;

internal static class SerialPortDeviceFactory
{
    public static DeviceInternal ToDeviceInternal(SerialPortInfo info)
    {
        var device = new DeviceInternal
        {
            Id = BuildId(info),
            DeviceType = DeviceType.Serial,
            DevicePath = info.CalloutPath,
            FriendlyName = BuildFriendlyName(info),
            VendorId = info.VendorId,
            ProductId = info.ProductId,
            SerialNumber = info.SerialNumber,
            Connected = true,
            LastUpdate = DateTime.UtcNow,
        };

        if (info.PortIndex >= 0)
        {
            device.DeviceData[DeviceDataKeys.UsbPortIndex] = info.PortIndex;
        }

        return device;
    }

    private static string BuildId(SerialPortInfo info)
    {
        if (info.HasUsbMetadata && info.PortIndex >= 0)
        {
            return $"SERIAL_{info.VendorId:X4}_{info.ProductId:X4}_P{info.PortIndex}_{info.SerialNumber}";
        }

        return info.CalloutPath;
    }

    private static string BuildFriendlyName(SerialPortInfo info)
    {
        if (!string.IsNullOrWhiteSpace(info.ProductName) && info.PortIndex >= 0)
        {
            return $"{info.ProductName} (port {info.PortIndex})";
        }

        if (!string.IsNullOrWhiteSpace(info.ProductName))
        {
            return info.ProductName;
        }

        return $"Serial Port {info.CalloutPath}";
    }
}
