using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Ape.Module.DeviceManager.Services.DeviceManagement.Serial;

/// <summary>macOS IORegistry-based USB serial enumeration.</summary>
internal sealed partial class MacSerialPortEnumerator : ISerialPortEnumerator
{
    public IReadOnlyList<SerialPortInfo> Enumerate()
    {
        var usbDevices = ParseUsbDevices(RunCommand("ioreg", "-p IOUSB -l -w 0"));
        var callouts = ParseCalloutDevices(RunCommand("ioreg", "-r -c IOSerialBSDClient -l"));

        var usbSerialCallouts = callouts
            .Where(static path => path.Contains("usbserial", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToList();

        if (usbSerialCallouts.Count > 0 && usbDevices.Count > 0)
        {
            var ftdi = usbDevices.FirstOrDefault(static d => d.VendorId == 0x0403)
                       ?? usbDevices[0];

            var results = new List<SerialPortInfo>(usbSerialCallouts.Count);
            for (var i = 0; i < usbSerialCallouts.Count; i++)
            {
                results.Add(new SerialPortInfo(
                    usbSerialCallouts[i],
                    ftdi.VendorId,
                    ftdi.ProductId,
                    ftdi.SerialNumber,
                    ftdi.ProductName,
                    i));
            }

            return results;
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
