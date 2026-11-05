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
            var resolved = ResolveCalloutPath(byIdPath);
            if (!File.Exists(resolved))
            {
                return false;
            }

            ttyPath = resolved;
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

    /// <summary>
    /// Walks from the tty <c>device</c> node to the USB device directory that holds <c>idVendor</c>.
    /// <c>/sys/class/tty/ttyACM*</c> and its <c>device</c> entry are relative symlinks, so the walk uses the physical path.
    /// </summary>
    internal static string? FindUsbDeviceDirectory(string startDir)
    {
        var current = new DirectoryInfo(Canonicalize(startDir));
        for (var depth = 0; depth < 8 && current != null; depth++, current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "idVendor"))
                && File.Exists(Path.Combine(current.FullName, "idProduct")))
            {
                return current.FullName;
            }
        }

        return null;
    }

    /// <summary>
    /// <c>Path.GetFullPath</c> keeps a symlink path as written.
    /// Relative link text is applied to the physical parent, which is what <c>/sys/class/tty</c> needs.
    /// </summary>
    private static string ResolveCalloutPath(string path) => Canonicalize(path);

    internal static string Canonicalize(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root))
        {
            return full;
        }

        var parts = full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var current = root;
        foreach (var part in parts)
        {
            current = Path.Combine(current, part);
            current = FollowLink(current);
        }

        return current.Length > root.Length
            ? current.TrimEnd(Path.DirectorySeparatorChar)
            : current;
    }

    private static string FollowLink(string path)
    {
        for (var hop = 0; hop < 16; hop++)
        {
            string? raw;
            try
            {
                raw = new FileInfo(path).LinkTarget;
            }
            catch (IOException)
            {
                return path;
            }
            catch (UnauthorizedAccessException)
            {
                return path;
            }

            if (string.IsNullOrEmpty(raw))
            {
                return path;
            }

            var parent = Path.GetDirectoryName(path.TrimEnd(Path.DirectorySeparatorChar)) ?? Path.GetPathRoot(path)!;
            var next = Path.IsPathRooted(raw)
                ? Path.GetFullPath(raw)
                : Path.GetFullPath(Path.Combine(parent, raw));
            if (string.Equals(next, path, StringComparison.Ordinal))
            {
                return path;
            }

            path = next;
        }

        return path;
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
