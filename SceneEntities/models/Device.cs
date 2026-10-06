using MessagePack;
using System;
using System.Collections.Generic;
using Ape.Core.Scene.Models;
using Ape.Module.DeviceManager.SceneEntities;

namespace Ape.Module.DeviceManager.SceneEntities.Models;

/// <summary>
/// Hardware device as a replicated scene <see cref="Entity"/>.
/// </summary>
[MessagePackObject(AllowPrivate = true)]
public partial class Device : Entity, IDevice
{
    public Device() : base(DeviceSceneEntityTypeIds.Device)
    {
    }

    [Key(10)]
    public DeviceType DeviceType { get; set; } = DeviceType.Unknown;

    [Key(11)]
    public int VendorId { get; set; }

    [Key(12)]
    public int ProductId { get; set; }

    [Key(13)]
    public string SerialNumber { get; set; } = string.Empty;

    [Key(14)]
    public string DevicePath { get; set; } = string.Empty;

    [Key(15)]
    public string FriendlyName { get; set; } = string.Empty;

    [Key(16)]
    public bool Connected { get; set; }

    [Key(17)]
    public DateTime LastUpdate { get; set; }

    [IgnoreMember]
    private Dictionary<string, object> _deviceData = new();

    [Key(18)]
    public Dictionary<string, object> DeviceData
    {
        get => _deviceData;
        set => SetProperty(ref _deviceData, value);
    }
}
