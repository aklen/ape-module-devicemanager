namespace Ape.Module.DeviceManager.Services.DeviceManagement.Serial;

/// <summary>USB serial port discovered on the host (callout path + metadata).</summary>
public sealed class SerialPortInfo
{
    public SerialPortInfo(
        string calloutPath,
        int vendorId = 0,
        int productId = 0,
        string serialNumber = "",
        string productName = "",
        int portIndex = -1)
    {
        CalloutPath = calloutPath;
        VendorId = vendorId;
        ProductId = productId;
        SerialNumber = serialNumber ?? "";
        ProductName = productName ?? "";
        PortIndex = portIndex;
    }

    /// <summary>OS path used for I/O (e.g. /dev/cu.*, /dev/ttyUSB0, COM3).</summary>
    public string CalloutPath { get; }

    public int VendorId { get; }
    public int ProductId { get; }
    public string SerialNumber { get; }
    public string ProductName { get; }

    /// <summary>FTDI-style interface port index (0 = port0), or -1 if unknown.</summary>
    public int PortIndex { get; }

    public bool HasUsbMetadata => VendorId != 0 || ProductId != 0;

    public static SerialPortInfo PathOnly(string calloutPath) => new(calloutPath);
}
