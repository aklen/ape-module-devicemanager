using System.Collections.Concurrent;
using Ape.Core.Config;
using Ape.Core.Config.Models;
using Ape.Core.Determinism;
using Ape.Core.Event;
using Ape.Core.Logging;
using Ape.Core.Network;
using Ape.Core.Replication;
using Ape.Core.Runtime.Plugin;
using Ape.Core.Scene;
using Ape.Core.Scene.Commit;
using Ape.Module.DeviceManager.SceneEntities;
using Ape.Module.DeviceManager.SceneEntities.Models;
using Ape.Module.DeviceManager.Services.DeviceManagement;

namespace Ape.Module.DeviceManager.Plugin.DeviceExample;

/// <summary>
/// Sample plugin: subscribe to serial devices, create <c>Device</c> replicas through
/// <see cref="IFrameCommitBatch"/>, open the port on the owner, observe on clients via <see cref="ISceneRead"/>.
/// Run: <c>./ape run -c src/Ape.Modules/Ape.Module.DeviceManager/Samples/device-example.json</c>
/// </summary>
public sealed class DeviceExamplePlugin : IPlugin, IDeterministicFrameParticipant
{
    public string PluginId => "device-example";
    public string Name => "Device Example Plugin";
    public string ParticipantId => PluginId;
    public FramePhase Phase => FramePhase.Publish;
    public int Order => 100;

    private readonly ConcurrentDictionary<string, DetectedSerial> _detected = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<SerialPayload> _payloads = new();
    private readonly ConcurrentDictionary<string, byte> _handlersOpened = new(StringComparer.Ordinal);
    private readonly HashSet<string> _created = new(StringComparer.Ordinal);

    private IDeviceManager? _deviceManager;
    private IEventManager? _eventManager;
    private ISceneRead? _sceneRead;
    private INetworkManager? _networkManager;
    private ILogger? _logger;
    private bool _authoritative;

    public void OnInit(IServiceProvider services)
    {
        _logger = services.GetService(typeof(ILogger)) as ILogger;
        _deviceManager = services.GetService(typeof(IDeviceManager)) as IDeviceManager;
        _eventManager = services.GetService(typeof(IEventManager)) as IEventManager;
        _sceneRead = services.GetService(typeof(ISceneRead)) as ISceneRead;
        _networkManager = services.GetService(typeof(INetworkManager)) as INetworkManager;
        var registry = services.GetService(typeof(IFrameParticipantRegistry)) as IFrameParticipantRegistry;

        if (_deviceManager == null)
        {
            _logger?.LogWarning($"[{Name}] IDeviceManager not available. Enable DeviceManagerService in host JSON.");
            return;
        }

        if (_sceneRead == null)
        {
            _logger?.LogWarning($"[{Name}] ISceneRead not available.");
            return;
        }

        _authoritative = _networkManager == null
            || !string.Equals(_networkManager.Role, "client", StringComparison.OrdinalIgnoreCase);

        registry?.Register(this);
        _eventManager?.Subscribe<PropertyChangedEvent>(PluginId, OnPropertyChanged);

        _logger?.LogInfo($"[{Name}] Local peer: {_networkManager?.LocalPeerId}, role: {_networkManager?.Role ?? "n/a"}");

        if (!_authoritative)
        {
            _logger?.LogInfo($"[{Name}] Client — observe Device replicas (no serial open).");
            return;
        }

        var startup = services.GetService(typeof(IStartupConfig)) as IStartupConfig;
        var moduleTable = services.GetService(typeof(IModuleTable)) as IModuleTable;
        var connection = DeviceConnectionConfig.TryRead(
            DeviceConnectionConfig.ServiceNode(startup?.Root, moduleTable));
        if (connection == null)
        {
            _logger?.LogInfo($"[{Name}] No device target in config — not opening ports or registering Device replicas.");
            return;
        }

        _deviceManager.Subscribe(
            connection,
            devices =>
            {
                foreach (var device in devices)
                {
                    var path = device.DevicePath;
                    if (string.IsNullOrWhiteSpace(path))
                        continue;

                    _detected[path] = new DetectedSerial(
                        path,
                        device.DeviceType,
                        device.FriendlyName,
                        device.VendorId,
                        device.ProductId);

                    if (!_handlersOpened.TryAdd(path, 0))
                        continue;

                    var handler = _deviceManager.GetDataHandler(device);
                    if (handler == null)
                        continue;

                    var sceneKey = path;
                    handler.DataReceived += (_, data) =>
                    {
                        var formatted = DataFormatter.Format(data, DataDisplayMode.Both);
                        _logger?.LogInfo($"[{Name}] {sceneKey}: {formatted}");
                        _payloads.Enqueue(new SerialPayload(
                            sceneKey,
                            new Dictionary<string, object>
                            {
                                ["lastData"] = System.Text.Encoding.UTF8.GetString(data),
                                ["lastDataHex"] = DataFormatter.Format(data, DataDisplayMode.HexOnly),
                                ["lastDataAscii"] = DataFormatter.Format(data, DataDisplayMode.AsciiOnly),
                                ["lastDataTime"] = DateTime.UtcNow,
                            },
                            DateTime.UtcNow));
                    };

                    _ = handler.OpenAsync();
                    _logger?.LogInfo($"[{Name}] Opened serial {path}");
                }
            });
    }

    public void OnHostFrame(in FrameContext context, IFrameCommitBatch commits)
    {
        if (!_authoritative)
            return;

        foreach (var detected in _detected.Values)
        {
            if (!_created.Add(detected.Path))
                continue;

            var owner = _networkManager?.LocalPeerId;
            commits.Enqueue(new CreateRegisteredEntityCommitRequest(
                DeviceSceneEntityTypeIds.Device,
                detected.Path,
                owner));
            commits.Enqueue(new SetSceneEntityPropertyCommitRequest(detected.Path, nameof(IDevice.DeviceType), detected.Type));
            commits.Enqueue(new SetSceneEntityPropertyCommitRequest(detected.Path, nameof(IDevice.DevicePath), detected.Path));
            commits.Enqueue(new SetSceneEntityPropertyCommitRequest(detected.Path, nameof(IDevice.FriendlyName), detected.FriendlyName));
            commits.Enqueue(new SetSceneEntityPropertyCommitRequest(detected.Path, nameof(IDevice.VendorId), detected.VendorId));
            commits.Enqueue(new SetSceneEntityPropertyCommitRequest(detected.Path, nameof(IDevice.ProductId), detected.ProductId));
            commits.Enqueue(new SetSceneEntityPropertyCommitRequest(detected.Path, nameof(IDevice.Connected), true));
            commits.Enqueue(new SetSceneEntityPropertyCommitRequest(detected.Path, nameof(IDevice.LastUpdate), DateTime.UtcNow));
            _logger?.LogInfo($"[{Name}] Queued Device replica for {detected.Path}");
        }

        while (_payloads.TryDequeue(out var payload))
        {
            commits.Enqueue(new SetSceneEntityPropertyCommitRequest(payload.SceneKey, nameof(IDevice.DeviceData), payload.Data));
            commits.Enqueue(new SetSceneEntityPropertyCommitRequest(payload.SceneKey, nameof(IDevice.LastUpdate), payload.Utc));
        }
    }

    public void OnRun(CancellationToken ct)
    {
        if (_deviceManager == null)
            _logger?.LogWarning($"[{Name}] Running without IDeviceManager");
        else
            _logger?.LogInfo($"[{Name}] Running (scene writes on host frames)");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                _eventManager?.DrainEventsFor(PluginId);
                Thread.Sleep(50);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[{Name}] Event loop: {ex.Message}");
                Thread.Sleep(100);
            }
        }
    }

    public void OnShutdown()
    {
        _eventManager?.Unsubscribe<PropertyChangedEvent>(PluginId);
        _logger?.LogInfo($"[{Name}] Shutdown");
    }

    private void OnPropertyChanged(PropertyChangedEvent evt)
    {
        if (_networkManager != null &&
            !string.Equals(_networkManager.Role, "client", StringComparison.OrdinalIgnoreCase))
            return;

        var device = _sceneRead?.GetEntity<IDevice>(evt.ReplicaId);
        if (device == null)
            return;

        _logger?.LogInfo($"[{Name}] Device {device.DevicePath} {evt.PropertyName} connected={device.Connected}");
        if (evt.PropertyName == nameof(IDevice.DeviceData) &&
            device.DeviceData.TryGetValue("lastDataAscii", out var ascii) &&
            ascii is string text)
        {
            _logger?.LogInfo($"[{Name}] lastDataAscii=\"{text}\"");
        }
    }

    private readonly record struct DetectedSerial(
        string Path,
        DeviceType Type,
        string FriendlyName,
        int VendorId,
        int ProductId);

    private readonly record struct SerialPayload(
        string SceneKey,
        Dictionary<string, object> Data,
        DateTime Utc);
}
