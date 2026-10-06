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
    /// Reads <c>serial</c> and <c>devices</c> from the <c>DeviceManagerService</c> object.
    /// Each entry needs <c>vendorId</c>+<c>productId</c> or <c>path</c>. Anything else is ignored.
    /// </summary>
    public static DeviceFilter? TryRead(IConfigNode? serviceNode)
    {
        if (serviceNode == null)
        {
            return null;
        }

        var filters = new List<DeviceFilter>();
        if (serviceNode.TryGetChildObject("serial", out var serial))
        {
            Add(filters, SerialPortFilterReader.TryParse(serial));
        }

        if (serviceNode.HasKey("devices"))
        {
            foreach (var entry in serviceNode.GetArray("devices"))
            {
                Add(filters, SerialPortFilterReader.TryParse(entry));
            }
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
