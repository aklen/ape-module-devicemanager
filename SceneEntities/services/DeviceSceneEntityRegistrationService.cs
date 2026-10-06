using Ape.Core.Logging;
using Ape.Core.Scene;
using Ape.Core.Runtime.Service;
using Ape.Module.DeviceManager.SceneEntities.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Ape.Module.DeviceManager.SceneEntities.Services;

/// <summary>
/// Registers the Device scene entity into <see cref="ISceneEntityRegistry"/>.
/// </summary>
public sealed class DeviceSceneEntityRegistrationService : IPluggableService
{
    public string ServiceId => "module-device-scene-entity";
    public string Name => "Device scene entity";

    public void Register(IServiceCollection serviceCollection)
    {
    }

    public void Initialize(IServiceProvider services)
    {
        var sceneEntityRegistry = services.GetRequiredService<ISceneEntityRegistry>();
        var logger = services.GetRequiredService<ILogger>();

        sceneEntityRegistry.Register(DeviceSceneEntityTypeIds.Device, (_, id, _) => new Device { Id = id });

        logger.LogDebug("DeviceManager module: registered Device scene entity factory.");
    }

    public void Start(CancellationToken cancellationToken)
    {
    }

    public void Stop()
    {
    }
}
