using System;
using System.Collections.Generic;
using Ape.Core.Logging;
using Ape.Core.Event;
using Ape.Core.Replication;
using Ape.Core.Network.FileTransfer;
using Ape.Core.Scene;
using Ape.Module.DeviceManager.SceneEntities;
using Ape.Module.DeviceManager.SceneEntities.Models;

namespace Ape.Module.DeviceManager.Services.DeviceManagement;

/// <summary>
/// Lightweight device representation for filtering purposes.
/// Does NOT inherit from Replica, so it won't trigger network synchronization.
/// Used by DeviceManager to test filter predicates before creating full Device instances.
/// 
/// This class implements IDevice interface with minimal dummy implementations for
/// the Replica-related properties (EventManager, Serialize, etc.) since they are not used
/// during filter predicate evaluation. Only the device-specific properties (VendorId,
/// ProductId, DevicePath, etc.) are populated with real values for filtering.
/// </summary>
internal class DeviceInternal : IDevice
{
    // IBase properties (from IEntity : IBase)
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UniquePath { get; } = string.Empty;
    public bool IsLocal { get; set; } = true;
    public string OwnerId { get; set; } = string.Empty;
    public object SyncRoot { get; } = new object();
    public IEventManager? EventManager { get; set; }
    public ILargeFileTransferService? LargeFileTransferService { get; set; }
    
    // IEntity properties
    public string TypeId { get; set; } = DeviceSceneEntityTypeIds.Device;
    
    // IDevice properties (actual values for filtering!)
    public DeviceType DeviceType { get; set; } = DeviceType.Unknown;
    public int VendorId { get; set; }
    public int ProductId { get; set; }
    public string SerialNumber { get; set; } = string.Empty;
    public string DevicePath { get; set; } = string.Empty;
    public string FriendlyName { get; set; } = string.Empty;
    public bool Connected { get; set; }
    public DateTime LastUpdate { get; set; }
    public Dictionary<string, object> DeviceData { get; set; } = new();
    
    // IReplica methods (dummy implementations)
    public byte[] Serialize() => Array.Empty<byte>();
    public void Deserialize(ReadOnlyMemory<byte> data) { }
    public void SnapshotProperties() { }
    public bool HasChanges() => false;
    public void NotifyPropertyChanged(string propertyName) { }
    
    // IBase methods (dummy implementations - DeviceInternal doesn't support hierarchy)
    public void SetParentNode(INode? parentNode) { }
    public INode? GetParentNode() => null;
    public void DetachFromParent() { }
    public List<IBase> GetChildren() => new List<IBase>();
    public bool HasChildren() => false;
}
