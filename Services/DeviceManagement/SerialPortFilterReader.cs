using Ape.Core.Config.Models;
using Ape.Module.DeviceManager.SceneEntities.Models;

namespace Ape.Module.DeviceManager.Services.DeviceManagement;

/// <summary>Builds a <see cref="DeviceFilter"/> for a serial or HID target in host JSON.</summary>
public static class SerialPortFilterReader
{
    public static DeviceFilter? TryParse(IConfigNode? serialNode, bool applyPortIndex = true)
    {
        if (serialNode == null)
        {
            return null;
        }

        if (!TryResolveType(serialNode, out var deviceType))
        {
            return null;
        }

        var path = FirstPath(serialNode);
        var vendorId = ParseHexId(serialNode.GetString("vendorId", ""));
        var productId = ParseHexId(serialNode.GetString("productId", ""));
        var hasUsbIds = vendorId != 0 && productId != 0;
        if (!hasUsbIds && string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var filter = hasUsbIds
            ? DeviceFilter.ByType(deviceType).And(DeviceFilter.ByVendorProduct(vendorId, productId))
            : DeviceFilter.ByType(deviceType).And(DeviceFilter.ByPath(path));

        if (hasUsbIds && !string.IsNullOrWhiteSpace(path))
        {
            filter = filter.And(DeviceFilter.ByPath(path));
        }

        if (deviceType == DeviceType.Serial && hasUsbIds && applyPortIndex && serialNode.HasKey("portIndex"))
        {
            filter = filter.And(DeviceFilter.ByPortIndex(serialNode.GetInt("portIndex")));
        }

        var serialNumber = serialNode.GetString("serialNumber", "");
        if (!string.IsNullOrWhiteSpace(serialNumber))
        {
            filter = filter.And(DeviceFilter.BySerial(serialNumber));
        }

        var productName = serialNode.GetString("productName", "");
        if (!string.IsNullOrWhiteSpace(productName))
        {
            filter = filter.And(DeviceFilter.ByProductName(productName));
        }

        return filter;
    }

    private static bool TryResolveType(IConfigNode node, out DeviceType deviceType)
    {
        deviceType = DeviceType.Unknown;
        if (!node.HasKey("type"))
        {
            return false;
        }

        var raw = node.GetString("type", "");
        if (raw.Equals("serial", StringComparison.OrdinalIgnoreCase))
        {
            deviceType = DeviceType.Serial;
            return true;
        }

        if (raw.Equals("hid", StringComparison.OrdinalIgnoreCase))
        {
            deviceType = DeviceType.HID;
            return true;
        }

        return false;
    }

    private static string FirstPath(IConfigNode serialNode)
    {
        var path = serialNode.GetString("path", "");
        return string.IsNullOrWhiteSpace(path) ? serialNode.GetString("devicePath", "") : path;
    }

    private static int ParseHexId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var text = value.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
        }

        return int.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var id) ? id : 0;
    }
}
