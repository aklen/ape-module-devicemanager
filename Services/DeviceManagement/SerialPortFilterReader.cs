using Ape.Core.Config.Models;
using Ape.Module.DeviceManager.SceneEntities.Models;

namespace Ape.Module.DeviceManager.Services.DeviceManagement;

/// <summary>Builds <see cref="DeviceFilter"/> for USB serial devices from host JSON.</summary>
public static class SerialPortFilterReader
{
    public static DeviceFilter? TryParse(IConfigNode? serialNode, bool applyPortIndex = true)
    {
        if (serialNode == null)
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
            ? DeviceFilter.ByType(DeviceType.Serial).And(DeviceFilter.ByVendorProduct(vendorId, productId))
            : DeviceFilter.ByType(DeviceType.Serial).And(DeviceFilter.ByPath(path));

        if (hasUsbIds && !string.IsNullOrWhiteSpace(path))
        {
            filter = filter.And(DeviceFilter.ByPath(path));
        }

        if (hasUsbIds && applyPortIndex && serialNode.HasKey("portIndex"))
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
