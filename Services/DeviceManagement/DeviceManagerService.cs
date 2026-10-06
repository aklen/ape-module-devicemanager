using Ape.Core.Config;
using Ape.Core.Config.Models;
using Ape.Core.Logging;
using Ape.Core.Scene;
using Ape.Core.Runtime.Service;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading;

namespace Ape.Module.DeviceManager.Services.DeviceManagement;

/// <summary>
/// Cross-platform device detection. Loaded by <c>ApeSystem</c> from this module's DLL when
/// the module is enabled in config; exposes <see cref="IDeviceManager"/> to plugins via DI.
/// </summary>
public sealed class DeviceManagerService : IPluggableService
{
    public string ServiceId => "device-manager";
    public string Name => "Device Manager Service";

    private DeviceManager? _deviceManager;
    private ISceneManager? _sceneManager;
    private ILogger? _logger;
    private bool _targetsConfigured;

    /// <summary>
    /// Register IDeviceManager interface in DI container.
    /// Called BEFORE ServiceProvider is built, so plugins can resolve it.
    /// </summary>
    public void Register(IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<IDeviceManager>(sp => _deviceManager
            ?? throw new InvalidOperationException("DeviceManager not initialized yet"));
    }

    /// <summary>
    /// Initialize the service by creating DeviceManager instance.
    /// Called AFTER ServiceProvider is built, so we can access ISceneManager, ILogger.
    /// </summary>
    public void Initialize(IServiceProvider services)
    {
        _sceneManager = services.GetService<ISceneManager>();
        _logger = services.GetService<ILogger>();

        if (_sceneManager == null || _logger == null)
        {
            throw new InvalidOperationException(
                $"[{Name}] Required services not found (ISceneManager, ILogger). " +
                "Ensure SceneManager is initialized before DeviceManagerService.");
        }

        _deviceManager = new DeviceManager(_sceneManager, _logger);
        var connection = DeviceConnectionConfig.TryRead(ReadServiceNode(services));
        _targetsConfigured = connection != null;
        _deviceManager.SetConnectionFilter(connection);
        if (!_targetsConfigured)
        {
            _logger.LogInfo($"[{Name}] No device target in config — not connecting or registering devices.");
        }

        _logger.LogDebug($"[{Name}] Initialized");
    }

    /// <summary>
    /// Start device monitoring with background polling.
    /// </summary>
    public void Start(CancellationToken cancellationToken)
    {
        if (_deviceManager == null)
        {
            _logger?.LogWarning($"[{Name}] Cannot start - not initialized");
            return;
        }

        if (!_targetsConfigured)
        {
            _logger?.LogInfo($"[{Name}] Device monitoring idle — waiting for a device target in config.");
            return;
        }

        _logger?.LogInfo($"[{Name}] Starting device monitoring (2s poll interval)...");
        _deviceManager.Start(pollIntervalMs: 2000);

        cancellationToken.Register(() => Stop());
    }

    /// <summary>
    /// Stop device monitoring and cleanup resources.
    /// </summary>
    public void Stop()
    {
        if (_deviceManager == null)
        {
            return;
        }

        _logger?.LogInfo($"[{Name}] Stopping device monitoring...");

        try
        {
            _deviceManager.Stop();
            _logger?.LogInfo($"[{Name}] ✅ Stopped");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning($"[{Name}] Stop error (ignored): {ex.Message}");
        }
    }

    private static IConfigNode? ReadServiceNode(IServiceProvider services)
    {
        var startup = services.GetService<IStartupConfig>();
        var moduleTable = services.GetService<IModuleTable>();
        return DeviceConnectionConfig.ServiceNode(startup?.Root, moduleTable);
    }
}
