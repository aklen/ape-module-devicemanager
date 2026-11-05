using Ape.Core.Logging;

namespace Ape.Module.DeviceManager.Services.DeviceManagement;

/// <summary>
/// Generic paced write loop for an open device link.
/// It does not know opcodes or payloads. The caller builds each tick.
/// A tick that is still running is skipped, so a slow peer is not flooded.
/// </summary>
public sealed class DeviceLinkPoller : IDisposable
{
    private readonly TimeSpan _interval;
    private readonly Func<CancellationToken, Task> _tick;
    private readonly ILogger? _logger;
    private readonly object _gate = new();

    private CancellationTokenSource? _cts;
    private Task? _loop;
    private int _tickRunning;
    private bool _disposed;

    public DeviceLinkPoller(TimeSpan interval, Func<CancellationToken, Task> tick, ILogger? logger = null)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "Poll interval must be positive.");
        }

        ArgumentNullException.ThrowIfNull(tick);
        _interval = interval;
        _tick = tick;
        _logger = logger;
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _loop != null)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            _loop = Task.Run(() => LoopAsync(_cts.Token));
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        Task? loop;
        lock (_gate)
        {
            cts = _cts;
            loop = _loop;
            _cts = null;
            _loop = null;
        }

        if (cts == null)
        {
            return;
        }

        try
        {
            cts.Cancel();
            loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            _logger?.LogDebug($"[DeviceLinkPoller] Stop: {ex.Message}");
        }
        finally
        {
            cts.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await RunTickAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await Task.Delay(_interval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunTickAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _tickRunning, 1) == 1)
        {
            _logger?.LogDebug("[DeviceLinkPoller] Previous tick still running, skipped.");
            return;
        }

        try
        {
            await _tick(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger?.LogWarning($"[DeviceLinkPoller] Tick failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _tickRunning, 0);
        }
    }
}
