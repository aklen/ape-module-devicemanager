using System.Globalization;
using System.IO.Ports;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Ape.Module.DeviceManager.Services.DeviceManagement.Serial;

/// <summary>Windows PnP enumeration that pairs each live COM port with its USB VID/PID.</summary>
internal sealed class WindowsSerialPortEnumerator : ISerialPortEnumerator
{
    private static readonly string[] EnumRoots =
    [
        @"SYSTEM\CurrentControlSet\Enum\USB",
        @"SYSTEM\CurrentControlSet\Enum\FTDIBUS",
    ];

    public IReadOnlyList<SerialPortInfo> Enumerate()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        try
        {
            var records = new List<WindowsPortRecord>();
            foreach (var rootPath in EnumRoots)
            {
                AppendRecords(rootPath, records);
            }

            var ports = WindowsUsbPortParser.SelectLive(records, SerialPort.GetPortNames());
            return ports.Count > 0
                ? ports
                : FallbackSerialPortEnumerator.EnumeratePathOnly();
        }
        catch
        {
            return FallbackSerialPortEnumerator.EnumeratePathOnly();
        }
    }

    private static void AppendRecords(string rootPath, List<WindowsPortRecord> records)
    {
        using var root = Registry.LocalMachine.OpenSubKey(rootPath);
        if (root == null)
        {
            return;
        }

        foreach (var hardwareId in root.GetSubKeyNames())
        {
            using var device = root.OpenSubKey(hardwareId);
            if (device == null)
            {
                continue;
            }

            foreach (var instanceId in device.GetSubKeyNames())
            {
                using var instance = device.OpenSubKey(instanceId);
                if (instance == null)
                {
                    continue;
                }

                using var parameters = instance.OpenSubKey("Device Parameters");
                var portName = parameters?.GetValue("PortName") as string;
                if (string.IsNullOrWhiteSpace(portName))
                {
                    continue;
                }

                var friendlyName = instance.GetValue("FriendlyName") as string ?? "";
                records.Add(new WindowsPortRecord(hardwareId, instanceId, portName.Trim(), friendlyName.Trim()));
            }
        }
    }
}

/// <summary>One USB serial instance read from the Windows device enum.</summary>
internal readonly record struct WindowsPortRecord(
    string HardwareId,
    string InstanceId,
    string PortName,
    string FriendlyName);

/// <summary>Turns Windows USB enum keys into <see cref="SerialPortInfo"/> for the ports that are live.</summary>
internal static partial class WindowsUsbPortParser
{
    internal static IReadOnlyList<SerialPortInfo> SelectLive(
        IReadOnlyList<WindowsPortRecord> records,
        IEnumerable<string> livePorts)
    {
        var remaining = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var port in livePorts)
        {
            if (string.IsNullOrWhiteSpace(port))
            {
                continue;
            }

            remaining.TryAdd(port.Trim(), port.Trim());
        }

        var results = new List<SerialPortInfo>();
        foreach (var record in records)
        {
            if (string.IsNullOrWhiteSpace(record.PortName)
                || !remaining.TryGetValue(record.PortName.Trim(), out var callout))
            {
                continue;
            }

            if (!TryParseHardwareId(record.HardwareId, out var vendorId, out var productId, out var portIndex, out var embeddedSerial)
                || vendorId == 0
                || productId == 0)
            {
                continue;
            }

            remaining.Remove(callout);
            results.Add(new SerialPortInfo(
                callout,
                vendorId,
                productId,
                ChooseSerial(record.InstanceId, embeddedSerial),
                ProductName(record.FriendlyName),
                portIndex));
        }

        foreach (var callout in remaining.Values)
        {
            results.Add(SerialPortInfo.PathOnly(callout));
        }

        return results
            .OrderBy(static info => info.CalloutPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static bool TryParseHardwareId(
        string hardwareId,
        out int vendorId,
        out int productId,
        out int portIndex,
        out string embeddedSerial)
    {
        vendorId = 0;
        productId = 0;
        portIndex = -1;
        embeddedSerial = "";

        var match = HardwareIdRegex().Match(hardwareId.Trim());
        if (!match.Success)
        {
            return false;
        }

        if (!int.TryParse(match.Groups[1].Value, NumberStyles.HexNumber, null, out vendorId)
            || !int.TryParse(match.Groups[2].Value, NumberStyles.HexNumber, null, out productId))
        {
            return false;
        }

        if (match.Groups[3].Success
            && int.TryParse(match.Groups[3].Value, NumberStyles.HexNumber, null, out var interfaceIndex))
        {
            portIndex = interfaceIndex;
        }

        if (match.Groups[4].Success)
        {
            embeddedSerial = match.Groups[4].Value;
        }

        return true;
    }

    internal static string ChooseSerial(string instanceId, string embeddedSerial)
    {
        var instance = instanceId.Trim();
        if (instance.Length > 0 && !GenericInstanceRegex().IsMatch(instance))
        {
            return instance;
        }

        return embeddedSerial.Trim();
    }

    internal static string ProductName(string friendlyName)
    {
        return ComSuffixRegex().Replace(friendlyName, "").Trim();
    }

    [GeneratedRegex(
        @"^VID_([0-9A-Fa-f]{4})[&+]PID_([0-9A-Fa-f]{4})(?:[&+]MI_([0-9A-Fa-f]{2}))?(?:[&+](.*))?$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex HardwareIdRegex();

    [GeneratedRegex(@"\s*\(COM\d+\)\s*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ComSuffixRegex();

    [GeneratedRegex(@"^\d{1,4}$", RegexOptions.CultureInvariant)]
    private static partial Regex GenericInstanceRegex();
}
