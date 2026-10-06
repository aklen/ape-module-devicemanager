using System.Text.RegularExpressions;

namespace Ape.Module.DeviceManager.Services.DeviceManagement.Serial;

/// <summary>Linux <c>/dev/serial/by-id</c> enumeration with sysfs VID/PID.</summary>
internal sealed partial class LinuxSerialPortEnumerator : ISerialPortEnumerator
{
    private const string ByIdDirectory = "/dev/serial/by-id";

    public IReadOnlyList<SerialPortInfo> Enumerate()
    {
        if (!Directory.Exists(ByIdDirectory))
        {
            return FallbackSerialPortEnumerator.EnumeratePathOnly();
        }

        var results = new List<SerialPortInfo>();
        foreach (var byIdPath in Directory.EnumerateFiles(ByIdDirectory).OrderBy(static p => p, StringComparer.Ordinal))
        {
            if (!TryResolveByIdLink(byIdPath, out var ttyPath, out var portIndex))
            {
                continue;
            }

            TryReadSysfsUsbIds(ttyPath, out var vendorId, out var productId, out var serialNumber);
            var productName = LinuxByIdParser.ParseProductName(Path.GetFileName(byIdPath));

            results.Add(new SerialPortInfo(
                ttyPath,
                vendorId,
                productId,
                serialNumber,
                productName,
                portIndex));
        }

        return results.Count > 0
            ? results
            : FallbackSerialPortEnumerator.EnumeratePathOnly();
    }

    internal static bool TryResolveByIdLink(string byIdPath, out string ttyPath, out int portIndex)
    {
        ttyPath = "";
        portIndex = LinuxByIdParser.ParsePortIndex(Path.GetFileName(byIdPath));

        try
        {
            var target = Path.GetFullPath(byIdPath);
            if (!File.Exists(target))
            {
                return false;
            }

            ttyPath = target;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void TryReadSysfsUsbIds(string ttyPath, out int vendorId, out int productId, out string serialNumber)
    {
        vendorId = 0;
        productId = 0;
        serialNumber = "";

        try
        {
            var ttyName = Path.GetFileName(ttyPath);
            var deviceDir = Path.Combine("/sys/class/tty", ttyName, "device");
            if (!Directory.Exists(deviceDir))
            {
                return;
            }

            var usbDir = FindUsbDeviceDirectory(deviceDir);
            if (usbDir == null)
            {
                return;
            }

            vendorId = ReadHexId(Path.Combine(usbDir, "idVendor"));
            productId = ReadHexId(Path.Combine(usbDir, "idProduct"));
            serialNumber = ReadTextFile(Path.Combine(usbDir, "serial"));
        }
        catch
        {
            // Best-effort metadata.
        }
    }

    private static string? FindUsbDeviceDirectory(string startDir)
    {
        var current = new DirectoryInfo(startDir);
        for (var depth = 0; depth < 6 && current != null; depth++, current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "idVendor"))
                && File.Exists(Path.Combine(current.FullName, "idProduct")))
            {
                return current.FullName;
            }
        }

        return null;
    }

    private static int ReadHexId(string path)
    {
        var text = ReadTextFile(path);
        return int.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var value) ? value : 0;
    }

    private static string ReadTextFile(string path)
    {
        return File.Exists(path) ? File.ReadAllText(path).Trim() : "";
    }
}

/// <summary>Parses Linux udev <c>/dev/serial/by-id</c> symlink names.</summary>
internal static partial class LinuxByIdParser
{
    [GeneratedRegex(@"-port(\d+)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex PortIndexRegex();

    internal static int ParsePortIndex(string byIdFileName)
    {
        var match = PortIndexRegex().Match(byIdFileName);
        return match.Success && int.TryParse(match.Groups[1].Value, out var portIndex) ? portIndex : -1;
    }

    internal static string ParseProductName(string byIdFileName)
    {
        // usb-FTDI_Dual_RS232-HS-if01-port0
        if (!byIdFileName.StartsWith("usb-", StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        var withoutPrefix = byIdFileName[4..];
        var interfaceIndex = withoutPrefix.IndexOf("-if", StringComparison.OrdinalIgnoreCase);
        if (interfaceIndex <= 0)
        {
            return withoutPrefix;
        }

        return withoutPrefix[..interfaceIndex].Replace('_', ' ');
    }
}
