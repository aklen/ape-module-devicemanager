using System.IO.Ports;

namespace Ape.Module.DeviceManager.Services.DeviceManagement.Serial;

internal static class FallbackSerialPortEnumerator
{
    public static IReadOnlyList<SerialPortInfo> EnumeratePathOnly()
    {
        if (OperatingSystem.IsMacOS())
        {
            return EnumerateMacCallout();
        }

        if (OperatingSystem.IsLinux())
        {
            return EnumerateLinuxTty();
        }

        if (OperatingSystem.IsWindows())
        {
            return SerialPort.GetPortNames()
                .Select(static name => SerialPortInfo.PathOnly(name))
                .ToList();
        }

        return [];
    }

    public static IReadOnlyList<SerialPortInfo> EnumerateMacCallout()
    {
        try
        {
            var devDirectory = new DirectoryInfo("/dev");
            if (!devDirectory.Exists)
            {
                return [];
            }

            return devDirectory.GetFiles("cu.*")
                .Select(static file => SerialPortInfo.PathOnly(file.FullName))
                .OrderBy(static info => info.CalloutPath, StringComparer.Ordinal)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<SerialPortInfo> EnumerateLinuxTty()
    {
        try
        {
            var devDirectory = new DirectoryInfo("/dev");
            if (!devDirectory.Exists)
            {
                return [];
            }

            var patterns = new[] { "ttyUSB*", "ttyACM*" };
            var results = new List<SerialPortInfo>();
            foreach (var pattern in patterns)
            {
                results.AddRange(devDirectory.GetFiles(pattern)
                    .Select(static file => SerialPortInfo.PathOnly(file.FullName)));
            }

            return results.OrderBy(static info => info.CalloutPath, StringComparer.Ordinal).ToList();
        }
        catch
        {
            return [];
        }
    }
}
