namespace Ape.Module.DeviceManager.Services.DeviceManagement;

/// <summary>
/// Interface for reading/writing data from/to physical devices.
/// Provides async I/O operations and data streaming via events.
///
/// Example usage:
///   var handler = deviceManager.GetDataHandler(deviceNode);
///   handler.DataReceived += (sender, data) => {
///       Console.WriteLine($"Received: {BitConverter.ToString(data)}");
///   };
///   await handler.OpenAsync();
///   await handler.WriteAsync(new byte[] { 0x01, 0x02, 0x03 });
/// </summary>
public interface IDeviceDataHandler : IDisposable
{
    /// <summary>
    /// Physical device path (e.g., /dev/ttyUSB0, COM3, HID device path)
    /// </summary>
    string DevicePath { get; }

    /// <summary>
    /// Whether the device connection is currently open
    /// </summary>
    bool IsOpen { get; }

    /// <summary>
    /// Event fired when data is received from the device.
    /// Data is provided as raw byte array.
    /// </summary>
    event EventHandler<byte[]>? DataReceived;

    /// <summary>
    /// Open the device connection for reading/writing.
    /// Must be called before Read/Write operations.
    /// </summary>
    Task OpenAsync();

    /// <summary>
    /// Close the device connection.
    /// </summary>
    Task CloseAsync();

    /// <summary>
    /// Write data to the device asynchronously.
    /// </summary>
    /// <param name="data">Raw bytes to write to the device</param>
    Task WriteAsync(byte[] data);

    /// <summary>
    /// Read data from the device asynchronously.
    /// </summary>
    /// <param name="maxBytes">Maximum number of bytes to read</param>
    /// <returns>Raw bytes read from the device</returns>
    Task<byte[]> ReadAsync(int maxBytes = 1024);

    /// <summary>
    /// Read exactly <paramref name="byteCount"/> bytes or throw on timeout.
    /// </summary>
    Task<byte[]> ReadExactAsync(int byteCount, CancellationToken cancellationToken = default);
}
