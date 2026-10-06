using Ape.Core.Logging;
using Ape.Core.Runtime.Service;
using HidSharp;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Ape.Module.DeviceManager.Services.DeviceManagement;

/// <summary>
/// HID device data handler for reading/writing HID device data.
/// Supports async I/O and automatic data reception via events.
/// 
/// Example usage:
///   var handler = new HidDataHandler(devicePath, logger);
///   handler.DataReceived += (sender, data) => {
///       Console.WriteLine($"Received HID report: {BitConverter.ToString(data)}");
///   };
///   await handler.OpenAsync();
///   await handler.WriteAsync(new byte[] { 0x00, 0x01, 0x02 });
/// </summary>
public class HidDataHandler : IDeviceDataHandler
{
    public string DevicePath { get; }
    public bool IsOpen => _stream != null;
    public event EventHandler<byte[]>? DataReceived;

    private HidStream? _stream;
    private readonly ILogger _logger;
    private CancellationTokenSource? _readCts;
    private Task? _readTask;

    public HidDataHandler(string devicePath, ILogger logger)
    {
        DevicePath = devicePath;
        _logger = logger;
    }

    public async Task OpenAsync()
    {
        try
        {
            // Find HID device by path
            var device = DeviceList.Local.GetHidDevices()
                .FirstOrDefault(d => d.DevicePath == DevicePath);

            if (device == null)
            {
                throw new InvalidOperationException($"HID device not found: {DevicePath}");
            }

            // Try to open the device
            if (!device.TryOpen(out _stream))
            {
                throw new InvalidOperationException($"Failed to open HID device: {DevicePath}");
            }

            _logger.LogInfo($"[HidDataHandler] Opened: {DevicePath} (VID: {device.VendorID:X4}, PID: {device.ProductID:X4})");

            // Start background read task
            _readCts = new CancellationTokenSource();
            _readTask = Task.Run(() => ReadLoopAsync(_readCts.Token));
        }
        catch (Exception ex)
        {
            _logger.LogError($"[HidDataHandler] Open failed ({DevicePath}): {ex.Message}");
            throw;
        }

        await Task.CompletedTask;
    }

    public async Task CloseAsync()
    {
        try
        {
            // Stop read loop
            _readCts?.Cancel();
            if (_readTask != null)
            {
                await _readTask.ConfigureAwait(false);
            }

            // Close stream
            _stream?.Close();
            _stream = null;

            _logger.LogInfo($"[HidDataHandler] Closed: {DevicePath}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"[HidDataHandler] Close error ({DevicePath}): {ex.Message}");
        }
    }

    public async Task WriteAsync(byte[] data)
    {
        if (_stream == null)
        {
            throw new InvalidOperationException($"HID device not open: {DevicePath}");
        }

        try
        {
            await _stream.WriteAsync(data, 0, data.Length);
            _logger.LogDebug($"[HidDataHandler] Wrote {data.Length} bytes to {DevicePath}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"[HidDataHandler] Write error ({DevicePath}): {ex.Message}");
            throw;
        }
    }

    public async Task<byte[]> ReadAsync(int maxBytes = 1024)
    {
        if (_stream == null)
        {
            return Array.Empty<byte>();
        }

        try
        {
            var buffer = new byte[maxBytes];
            var bytesRead = await _stream.ReadAsync(buffer, 0, maxBytes);

            if (bytesRead > 0 && bytesRead < maxBytes)
            {
                Array.Resize(ref buffer, bytesRead);
            }

            return buffer;
        }
        catch (Exception ex)
        {
            _logger.LogError($"[HidDataHandler] Read error ({DevicePath}): {ex.Message}");
            return Array.Empty<byte>();
        }
    }

    public async Task<byte[]> ReadExactAsync(int byteCount, CancellationToken cancellationToken = default)
    {
        if (_stream == null)
        {
            throw new InvalidOperationException($"HID device not open: {DevicePath}");
        }

        var buffer = new byte[byteCount];
        var offset = 0;
        var deadline = DateTime.UtcNow.AddMilliseconds(500);

        while (offset < byteCount)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chunk = await ReadAsync(byteCount - offset);
            if (chunk.Length == 0)
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException(
                        $"Timed out reading {byteCount} bytes from {DevicePath} (got {offset})");
                }

                await Task.Delay(5, cancellationToken);
                continue;
            }

            Array.Copy(chunk, 0, buffer, offset, chunk.Length);
            offset += chunk.Length;
        }

        return buffer;
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        if (_stream == null)
        {
            return;
        }

        var buffer = new byte[_stream.Device.GetMaxInputReportLength()];

        while (!ct.IsCancellationRequested && _stream != null)
        {
            try
            {
                var bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, ct);

                if (bytesRead > 0)
                {
                    var data = new byte[bytesRead];
                    Array.Copy(buffer, data, bytesRead);
                    DataReceived?.Invoke(this, data);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[HidDataHandler] Read loop error ({DevicePath}): {ex.Message}");
                await Task.Delay(100, ct); // Avoid tight loop on persistent errors
            }
        }

        _logger.LogDebug($"[HidDataHandler] Read loop stopped: {DevicePath}");
    }

    public void Dispose()
    {
        _readCts?.Cancel();
        _readTask?.Wait(TimeSpan.FromSeconds(1));
        _readCts?.Dispose();
        _stream?.Close();
        _stream = null;
    }
}
