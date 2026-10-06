using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Ape.Module.DeviceManager.Services.DeviceManagement.Serial;

/// <summary>macOS IORegistry-based USB serial enumeration.</summary>
internal sealed partial class MacSerialPortEnumerator : ISerialPortEnumerator
{
    public IReadOnlyList<SerialPortInfo> Enumerate()
    {
        var paired = ParseUsbSerialCallouts(RunCommand("ioreg", "-r -c IOUSBHostDevice -l -w 0"));
        if (paired.Count > 0)
        {
            return paired;
        }

        return FallbackSerialPortEnumerator.EnumerateMacCallout();
    }

    internal static IReadOnlyList<UsbDeviceRecord> ParseUsbDevices(string ioregOutput)
    {
        var results = new List<UsbDeviceRecord>();
        if (string.IsNullOrWhiteSpace(ioregOutput))
        {
            return results;
        }

        var blocks = ioregOutput.Split("+-o ", StringSplitOptions.RemoveEmptyEntries);
        foreach (var block in blocks)
        {
            var vendorId = ParseHexProperty(block, "idVendor");
            var productId = ParseHexProperty(block, "idProduct");
            if (vendorId == 0 || productId == 0)
            {
                continue;
            }

            var productName = ParseStringProperty(block, "USB Product Name")
                              ?? ParseStringProperty(block, "kUSBProductString")
                              ?? "";

            var serialNumber = ParseStringProperty(block, "USB Serial Number") ?? "";
            results.Add(new UsbDeviceRecord(vendorId, productId, productName, serialNumber));
        }

        return results;
    }

    /// <summary>
    /// Pairs each <c>/dev/cu.*</c> callout with the USB device above it in the registry tree.
    /// Covers CDC <c>usbmodem</c> ports as well as <c>usbserial</c>.
    /// </summary>
    internal static IReadOnlyList<SerialPortInfo> ParseUsbSerialCallouts(string ioregOutput)
    {
        var results = new List<SerialPortInfo>();
        if (string.IsNullOrWhiteSpace(ioregOutput))
        {
            return results;
        }

        var vendorId = 0;
        var productId = 0;
        var productName = "";
        var serialNumber = "";
        var portIndex = 0;

        foreach (var line in ioregOutput.Split('\n'))
        {
            if (line.Contains("<class IOUSBHostDevice", StringComparison.Ordinal)
                || line.Contains("<class IOUSBDevice", StringComparison.Ordinal))
            {
                vendorId = 0;
                productId = 0;
                productName = "";
                serialNumber = "";
                portIndex = 0;
            }

            var vendor = ParseHexProperty(line, "idVendor");
            if (vendor != 0)
            {
                vendorId = vendor;
            }

            var product = ParseHexProperty(line, "idProduct");
            if (product != 0)
            {
                productId = product;
            }

            var name = ParseStringProperty(line, "USB Product Name");
            if (!string.IsNullOrEmpty(name))
            {
                productName = name;
            }

            var serial = ParseStringProperty(line, "USB Serial Number");
            if (!string.IsNullOrEmpty(serial))
            {
                serialNumber = serial;
            }

            var path = ParseStringProperty(line, "IOCalloutDevice");
            if (path == null || !path.StartsWith("/dev/cu.", StringComparison.Ordinal))
            {
                continue;
            }

            if (vendorId == 0 || productId == 0)
            {
                continue;
            }

            results.Add(new SerialPortInfo(path, vendorId, productId, serialNumber, productName, portIndex));
            portIndex++;
        }

        return results;
    }

    internal static IReadOnlyList<string> ParseCalloutDevices(string ioregOutput)
    {
        var results = new List<string>();
        if (string.IsNullOrWhiteSpace(ioregOutput))
        {
            return results;
        }

        foreach (Match match in CalloutRegex().Matches(ioregOutput))
        {
            var path = match.Groups[1].Value;
            if (path.StartsWith("/dev/cu.", StringComparison.Ordinal))
            {
                results.Add(path);
            }
        }

        return results;
    }

    [GeneratedRegex("\"IOCalloutDevice\" = \"(/dev/cu\\.[^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex CalloutRegex();

    private static int ParseHexProperty(string block, string key)
    {
        var match = Regex.Match(block, $"\"{Regex.Escape(key)}\" = (\\d+)", RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(match.Groups[1].Value, out var value) ? value : 0;
    }

    private static string? ParseStringProperty(string block, string key)
    {
        var match = Regex.Match(block, $"\"{Regex.Escape(key)}\" = \"([^\"]*)\"", RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string RunCommand(string fileName, string arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(TimeSpan.FromSeconds(5));
            return output;
        }
        catch
        {
            return "";
        }
    }

    internal sealed record UsbDeviceRecord(int VendorId, int ProductId, string ProductName, string SerialNumber);
}
