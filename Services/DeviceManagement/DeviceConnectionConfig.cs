using Ape.Core.Config.Models;

namespace Ape.Module.DeviceManager.Services.DeviceManagement;

/// <summary>
/// Devices the manager may open or publish. An empty service object yields no targets,
/// so discovery, port open, and scene registration stay closed.
/// </summary>
public static class DeviceConnectionConfig
{
    public const string ServiceKey = "DeviceManagerService";

    public static IConfigNode? ServiceNode(IConfigNode? root, IModuleTable? moduleTable)
    {
        var section = moduleTable?.GetModuleSection(root, DeviceManagerModuleIds.ModuleId);
        if (section == null || !section.TryGetChildObject("services", out var services))
        {
            return null;
        }

        return services.TryGetChildObject(ServiceKey, out var service) ? service : null;
    }

    /// <summary>
    /// Reads <c>devices</c> from the <c>DeviceManagerService</c> object.
    /// Each entry needs <c>type</c> (<c>serial</c> or <c>hid</c>) and either <c>vendorId</c>+<c>productId</c> or <c>path</c>.
    /// Optional <c>description</c> is a label for the user. It does not filter.
    /// Entries without a supported type are ignored.
    /// </summary>
    public static IReadOnlyList<DeviceTarget> ReadTargets(IConfigNode? serviceNode)
    {
        if (serviceNode == null || !serviceNode.HasKey("devices"))
        {
            return [];
        }

        var targets = new List<DeviceTarget>();
        foreach (var entry in serviceNode.GetArray("devices"))
        {
            var filter = SerialPortFilterReader.TryParse(entry);
            if (filter == null)
            {
                continue;
            }

            targets.Add(new DeviceTarget(filter, entry.GetString("description", "").Trim()));
        }

        return targets;
    }

    public static DeviceFilter? TryRead(IConfigNode? serviceNode)
    {
        var filters = new List<DeviceFilter>();
        foreach (var target in ReadTargets(serviceNode))
        {
            Add(filters, target.Filter);
        }

        if (filters.Count == 0)
        {
            return null;
        }

        var combined = filters[0];
        for (var i = 1; i < filters.Count; i++)
        {
            combined = combined.Or(filters[i]);
        }

        return combined;
    }

    private static void Add(List<DeviceFilter> filters, DeviceFilter? filter)
    {
        if (filter != null)
        {
            filters.Add(filter);
        }
    }
}

/// <summary>One host-JSON device entry: match rule plus an optional user label.</summary>
public sealed record DeviceTarget(DeviceFilter Filter, string Description);
