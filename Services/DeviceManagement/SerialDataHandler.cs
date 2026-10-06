using Ape.Core.Logging;
using Ape.Core.Runtime.Service;
using System;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace Ape.Module.DeviceManager.Services.DeviceManagement;

/// <summary>
/// Serial port data handler for reading/writing serial device data.
/// Supports async I/O and automatic data reception via events.
/// Cross-platform: Uses System.IO.Ports on Windows, FileStream on Unix.
/// 
/// Example usage:
///   var handler = new SerialDataHandler("/dev/ttyUSB0", logger);
///   handler.DataReceived += (sender, data) => {
///       Console.WriteLine($"Received: {System.Text.Encoding.ASCII.GetString(data)}");
///   };
///   await handler.OpenAsync();
///   await handler.WriteAsync(System.Text.Encoding.ASCII.GetBytes("Hello Arduino\n"));
/// </summary>
public class SerialDataHandler : IDeviceDataHandler
{
    public string DevicePath { get; }
    public bool IsOpen => _isOpen;
    public event EventHandler<byte[]>? DataReceived;

    // Windows (System.IO.Ports)
    private SerialPort? _port;
    
    // Unix (FileStream)
    private FileStream? _fileStream;
    private Task? _readTask;
    private CancellationTokenSource? _readCts;
    private bool _isOpen;
    
    private readonly ILogger _logger;
    private readonly int _baudRate;
    private readonly Parity _parity;
    private readonly int _dataBits;
    private readonly StopBits _stopBits;
    private readonly int _readTimeoutMs;
    private readonly int _writeTimeoutMs;
    private readonly bool _useSerialPort;
    private readonly bool _synchronousOnly;

    /// <summary>
    /// Create a serial data handler with default settings (9600 baud, 8N1).
    /// </summary>
    public SerialDataHandler(string devicePath, ILogger logger)
        : this(devicePath, logger, new SerialPortOptions())
    {
    }

    /// <summary>
    /// Create a serial data handler with custom settings.
    /// </summary>
    public SerialDataHandler(string devicePath, ILogger logger, int baudRate, Parity parity, int dataBits, StopBits stopBits)
        : this(devicePath, logger, new SerialPortOptions
        {
            BaudRate = baudRate,
            Parity = parity,
            DataBits = dataBits,
            StopBits = stopBits,
        })
    {
    }

    /// <summary>
    /// Create a serial data handler with explicit line settings from <see cref="SerialPortOptions"/>.
    /// </summary>
    public SerialDataHandler(string devicePath, ILogger logger, SerialPortOptions options)
    {
        DevicePath = devicePath;
        _logger = logger;
        _baudRate = options.BaudRate;
        _parity = options.Parity;
        _dataBits = options.DataBits;
        _stopBits = options.StopBits;
        _readTimeoutMs = options.ReadTimeoutMs;
        _writeTimeoutMs = options.WriteTimeoutMs;
        _synchronousOnly = _baudRate != 9600 || _parity != Parity.None;
        _useSerialPort = OperatingSystem.IsWindows() || _synchronousOnly;
    }

    public Task OpenAsync()
    {
        try
        {
            if (_useSerialPort)
            {
                _port = new SerialPort(DevicePath, _baudRate, _parity, _dataBits, _stopBits)
                {
                    ReadTimeout = _readTimeoutMs,
                    WriteTimeout = _writeTimeoutMs,
                };

                if (!_synchronousOnly)
                {
                    _port.DataReceived += OnWindowsDataReceived;
                }

                _port.Open();
                _isOpen = true;

                if (_synchronousOnly)
                {
                    _readCts = new CancellationTokenSource();
                    _readTask = Task.Run(() => SerialPortReadLoopAsync(_readCts.Token));
                }

                _logger.LogInfo(
                    $"[SerialDataHandler] Opened {DevicePath} ({_baudRate} baud, {_dataBits}{_parity.ToString()[0]}{(int)_stopBits})");
            }
            else
            {
                // Unix: Use FileStream (note: baud rate configuration would require termios, but for testing we'll just open the device)
                // For character devices (e.g. /dev/cu.usbmodem*), FileStream's async ReadAsync path can
                // throw ArgumentOutOfRangeException on some runtimes/OS combinations. We therefore use
                // a synchronous read loop on a background task for robustness.
                _fileStream = new FileStream(
                    DevicePath,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.ReadWrite);
                _isOpen = true;
                
                // Start background read task
                _readCts = new CancellationTokenSource();
                _readTask = Task.Run(() => UnixReadLoopAsync(_readCts.Token));
                
                _logger.LogInfo($"[SerialDataHandler] Unix: Opened {DevicePath} (FileStream mode)");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"[SerialDataHandler] Open failed ({DevicePath}): {ex.Message}");
            throw;
        }
        
        return Task.CompletedTask;
    }

    public Task CloseAsync()
    {
        try
        {
            _isOpen = false;
            
            if (_port != null && _port.IsOpen)
            {
                StopReadLoop();

                if (!_synchronousOnly)
                {
                    _port.DataReceived -= OnWindowsDataReceived;
                }

                _port.Close();
                _logger.LogInfo($"[SerialDataHandler] Closed {DevicePath}");
            }
            
            if (_fileStream != null)
            {
                StopUnixReadLoop();
                _logger.LogInfo($"[SerialDataHandler] Unix: Closed {DevicePath}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"[SerialDataHandler] Close error ({DevicePath}): {ex.Message}");
        }
        
        return Task.CompletedTask;
    }

    public Task WriteAsync(byte[] data)
    {
        if (!_isOpen)
        {
            throw new InvalidOperationException($"Serial port not open: {DevicePath}");
        }

        try
        {
            if (_port != null && _port.IsOpen)
            {
                // Windows
                _port.Write(data, 0, data.Length);
            }
            else if (_fileStream != null)
            {
                // Unix
                _fileStream.Write(data, 0, data.Length);
                _fileStream.Flush();
            }
            
            _logger.LogDebug($"[SerialDataHandler] Wrote {data.Length} bytes to {DevicePath}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"[SerialDataHandler] Write error ({DevicePath}): {ex.Message}");
            throw;
        }
        
        return Task.CompletedTask;
    }

    public Task<byte[]> ReadAsync(int maxBytes = 1024)
    {
        if (!_isOpen)
        {
            return Task.FromResult(Array.Empty<byte>());
        }

        try
        {
            if (_port != null && _port.IsOpen)
            {
                // Windows
                var bytesToRead = Math.Min(_port.BytesToRead, maxBytes);
                if (bytesToRead == 0)
                {
                    return Task.FromResult(Array.Empty<byte>());
                }

                var buffer = new byte[bytesToRead];
                var bytesRead = _port.Read(buffer, 0, bytesToRead);
                
                if (bytesRead < bytesToRead)
                {
                    Array.Resize(ref buffer, bytesRead);
                }
                
                return Task.FromResult(buffer);
            }
            else if (_fileStream != null)
            {
                // Unix: Non-blocking read (this is simplified - real implementation might need select/poll)
                var buffer = new byte[maxBytes];
                var bytesRead = _fileStream.Read(buffer, 0, maxBytes);
                
                if (bytesRead < maxBytes)
                {
                    Array.Resize(ref buffer, bytesRead);
                }
                
                return Task.FromResult(buffer);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"[SerialDataHandler] Read error ({DevicePath}): {ex.Message}");
        }
        
        return Task.FromResult(Array.Empty<byte>());
    }

    public async Task<byte[]> ReadExactAsync(int byteCount, CancellationToken cancellationToken = default)
    {
        if (!_isOpen)
        {
            throw new InvalidOperationException($"Serial port not open: {DevicePath}");
        }

        var buffer = new byte[byteCount];
        var offset = 0;
        var deadline = DateTime.UtcNow.AddMilliseconds(_readTimeoutMs);

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

    private void OnWindowsDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        if (_port == null || !_port.IsOpen)
        {
            return;
        }

        try
        {
            var bytesToRead = _port.BytesToRead;
            if (bytesToRead > 0)
            {
                var buffer = new byte[bytesToRead];
                var bytesRead = _port.Read(buffer, 0, bytesToRead);
                
                if (bytesRead > 0)
                {
                    if (bytesRead < bytesToRead)
                    {
                        Array.Resize(ref buffer, bytesRead);
                    }
                    
                    DataReceived?.Invoke(this, buffer);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"[SerialDataHandler] Windows data receive error ({DevicePath}): {ex.Message}");
        }
    }

    private async Task SerialPortReadLoopAsync(CancellationToken cancellationToken)
    {
        if (_port == null)
        {
            return;
        }

        var buffer = new byte[1024];
        var consecutiveErrors = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested && _isOpen)
            {
                try
                {
                    var bytesRead = _port.Read(buffer, 0, buffer.Length);

                    if (bytesRead > 0)
                    {
                        consecutiveErrors = 0;
                        var data = new byte[bytesRead];
                        Array.Copy(buffer, data, bytesRead);
                        DataReceived?.Invoke(this, data);
                    }

                    await Task.Delay(10, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (TimeoutException)
                {
                    await Task.Delay(10, cancellationToken);
                }
                catch (Exception ex)
                {
                    consecutiveErrors++;
                    _logger.LogError(
                        $"[SerialDataHandler] Serial read loop error ({DevicePath}): {ex.GetType().Name}: {ex.Message}");

                    var backoffMs = Math.Min(2000, 100 * consecutiveErrors);
                    await Task.Delay(backoffMs, cancellationToken);
                    if (consecutiveErrors >= 20)
                    {
                        _logger.LogError(
                            $"[SerialDataHandler] Serial read loop stopping after {consecutiveErrors} consecutive errors ({DevicePath}).");
                        _isOpen = false;
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
        catch (Exception ex)
        {
            _logger.LogError($"[SerialDataHandler] Serial read loop fatal error ({DevicePath}): {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task UnixReadLoopAsync(CancellationToken cancellationToken)
    {
        if (_fileStream == null)
        {
            return;
        }

        var buffer = new byte[1024];
        var consecutiveErrors = 0;
        
        try
        {
            while (!cancellationToken.IsCancellationRequested && _isOpen)
            {
                try
                {
                    // Robust path for TTY-like devices: synchronous read in background task.
                    // NOTE: This call may block; cancellation is cooperative via _isOpen and token checks.
                    var bytesRead = _fileStream.Read(buffer, 0, buffer.Length);
                    
                    if (bytesRead > 0)
                    {
                        consecutiveErrors = 0;
                        var data = new byte[bytesRead];
                        Array.Copy(buffer, data, bytesRead);
                        DataReceived?.Invoke(this, data);
                    }
                    
                    // Small delay to prevent busy waiting
                    await Task.Delay(10, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    consecutiveErrors++;
                    _logger.LogError(
                        $"[SerialDataHandler] Unix read loop error ({DevicePath}): {ex.GetType().Name}: {ex.Message}\n{ex}");

                    // Back off on error; if we keep failing, stop the loop to avoid log spam.
                    var backoffMs = Math.Min(2000, 100 * consecutiveErrors);
                    await Task.Delay(backoffMs, cancellationToken);
                    if (consecutiveErrors >= 20)
                    {
                        _logger.LogError(
                            $"[SerialDataHandler] Unix read loop stopping after {consecutiveErrors} consecutive errors ({DevicePath}).");
                        _isOpen = false;
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
        catch (Exception ex)
        {
            _logger.LogError($"[SerialDataHandler] Unix read loop fatal error ({DevicePath}): {ex.GetType().Name}: {ex.Message}\n{ex}");
        }
    }

    public void Dispose()
    {
        _isOpen = false;
        
        if (_port != null)
        {
            StopReadLoop();

            if (_port.IsOpen)
            {
                if (!_synchronousOnly)
                {
                    _port.DataReceived -= OnWindowsDataReceived;
                }

                _port.Close();
            }
            _port.Dispose();
            _port = null;
        }
        
        StopUnixReadLoop();
    }

    private void StopReadLoop()
    {
        if (_readCts == null)
            return;

        try
        {
            _readCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // already torn down
        }

        try
        {
            _readTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // ignore read-loop shutdown races
        }

        _readCts.Dispose();
        _readCts = null;
        _readTask = null;
    }

    private void StopUnixReadLoop()
    {
        if (_fileStream == null)
            return;

        _isOpen = false;
        StopReadLoop();
        try
        {
            _fileStream.Close();
        }
        catch
        {
            // ignore close races
        }

        _fileStream.Dispose();
        _fileStream = null;
    }
}
