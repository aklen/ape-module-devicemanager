namespace Ape.Module.DeviceManager.Services.DeviceManagement.Serial;

/// <summary>Platform-specific USB serial port enumeration.</summary>
public interface ISerialPortEnumerator
{
    IReadOnlyList<SerialPortInfo> Enumerate();
}
