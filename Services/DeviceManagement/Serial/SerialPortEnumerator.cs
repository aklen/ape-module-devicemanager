namespace Ape.Module.DeviceManager.Services.DeviceManagement.Serial;

internal static class SerialPortEnumerator
{
    public static ISerialPortEnumerator CreateDefault() =>
        OperatingSystem.IsLinux()
            ? new LinuxSerialPortEnumerator()
            : OperatingSystem.IsMacOS()
                ? new MacSerialPortEnumerator()
                : new PassthroughSerialPortEnumerator(FallbackSerialPortEnumerator.EnumeratePathOnly());

    internal sealed class PassthroughSerialPortEnumerator(IReadOnlyList<SerialPortInfo> ports) : ISerialPortEnumerator
    {
        public IReadOnlyList<SerialPortInfo> Enumerate() => ports;
    }
}
