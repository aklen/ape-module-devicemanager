using Ape.Module.DeviceManager.Services.DeviceManagement;

namespace Ape.Module.DeviceManager.Tests;

public class DeviceLinkPollerTests
{
    [Fact]
    public async Task Start_invokes_the_tick_then_waits()
    {
        var count = 0;
        using var poller = new DeviceLinkPoller(TimeSpan.FromMilliseconds(40), _ =>
        {
            Interlocked.Increment(ref count);
            return Task.CompletedTask;
        });

        poller.Start();
        await Task.Delay(150);
        poller.Stop();

        Assert.InRange(count, 2, 8);
    }
}
