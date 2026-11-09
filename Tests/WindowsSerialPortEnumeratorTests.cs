using System.IO.Ports;
using Ape.Module.DeviceManager.SceneEntities.Models;
using Ape.Module.DeviceManager.Services.DeviceManagement;
using Ape.Module.DeviceManager.Services.DeviceManagement.Serial;
using Microsoft.Win32;

namespace Ape.Module.DeviceManager.Tests;

public class WindowsUsbPortParserTests
{
    [Fact]
    public void SelectLive_reads_smc_ids_and_drops_unplugged_boards()
    {
        WindowsPortRecord[] records =
        [
            new("VID_0483&PID_5741", "208839AA4345", "COM17", "USB Serial Device (COM17)"),
            new("VID_0483&PID_5741", "205536755742", "COM18", "USB Serial Device (COM18)"),
            new("VID_0483&PID_5741", "205539854345", "COM22", "USB Serial Device (COM22)"),
        ];

        var ports = WindowsUsbPortParser.SelectLive(records, ["COM11", "COM17"]);

        var smc = Assert.Single(ports, port => port.CalloutPath == "COM17");
        Assert.Equal(0x0483, smc.VendorId);
        Assert.Equal(0x5741, smc.ProductId);
        Assert.Equal("208839AA4345", smc.SerialNumber);
        Assert.Equal("USB Serial Device", smc.ProductName);
        Assert.Equal(-1, smc.PortIndex);

        var amt = Assert.Single(ports, port => port.CalloutPath == "COM11");
        Assert.False(amt.HasUsbMetadata);
        Assert.DoesNotContain(ports, port => port.CalloutPath is "COM18" or "COM22");
    }

    [Fact]
    public void SelectLive_matches_the_vendor_product_filter()
    {
        var ports = WindowsUsbPortParser.SelectLive(
            [new WindowsPortRecord("VID_0483&PID_5741", "208839AA4345", "COM17", "USB Serial Device (COM17)")],
            ["COM17"]);
        var device = SerialPortDeviceFactory.ToDeviceInternal(Assert.Single(ports));

        var filter = DeviceFilter.ByType(DeviceType.Serial)
            .And(DeviceFilter.ByVendorProduct(0x0483, 0x5741));

        Assert.True(filter.Predicate(device));
    }

    [Fact]
    public void SelectLive_keeps_two_live_boards_of_the_same_ids()
    {
        WindowsPortRecord[] records =
        [
            new("VID_0483&PID_5741", "208839AA4345", "COM17", "USB Serial Device (COM17)"),
            new("VID_0483&PID_5741", "205536755742", "COM18", "USB Serial Device (COM18)"),
        ];

        var ports = WindowsUsbPortParser.SelectLive(records, ["COM17", "COM18"]);

        Assert.Equal(2, ports.Count);
        Assert.All(ports, port => Assert.Equal(0x0483, port.VendorId));
        Assert.All(ports, port => Assert.Equal(0x5741, port.ProductId));
        Assert.Contains(ports, port => port.SerialNumber == "208839AA4345");
        Assert.Contains(ports, port => port.SerialNumber == "205536755742");
    }

    [Fact]
    public void SelectLive_reads_interface_index_and_ftdi_serial()
    {
        WindowsPortRecord[] records =
        [
            new("VID_0483&PID_5741&MI_01", "208839AA4345", "COM4", "USB Serial Device (COM4)"),
            new("VID_0403+PID_6001+A6008h5A", "0000", "COM5", "USB Serial Port (COM5)"),
        ];

        var ports = WindowsUsbPortParser.SelectLive(records, ["COM4", "COM5"]);

        var cdc = Assert.Single(ports, port => port.CalloutPath == "COM4");
        Assert.Equal(1, cdc.PortIndex);
        Assert.Equal("208839AA4345", cdc.SerialNumber);

        var ftdi = Assert.Single(ports, port => port.CalloutPath == "COM5");
        Assert.Equal(0x0403, ftdi.VendorId);
        Assert.Equal(0x6001, ftdi.ProductId);
        Assert.Equal("A6008h5A", ftdi.SerialNumber);
        Assert.Equal("USB Serial Port", ftdi.ProductName);
        Assert.Equal(-1, ftdi.PortIndex);
    }

    [Fact]
    public void SelectLive_ignores_ports_that_are_not_open()
    {
        var ports = WindowsUsbPortParser.SelectLive(
            [new WindowsPortRecord("VID_0483&PID_5741", "208839AA4345", "COM17", "USB Serial Device (COM17)")],
            []);

        Assert.Empty(ports);
    }
}

public class WindowsSerialPortEnumeratorTests
{
    [Fact]
    public void Enumerate_includes_the_connected_smc_port()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var live = SerialPort.GetPortNames().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expected = ConnectedPort(@"SYSTEM\CurrentControlSet\Enum\USB\VID_0483&PID_5741", live);
        if (expected == null)
        {
            return;
        }

        var ports = new WindowsSerialPortEnumerator().Enumerate();
        var match = Assert.Single(ports, port =>
            port.CalloutPath.Equals(expected.Value.PortName, StringComparison.OrdinalIgnoreCase));

        Assert.Equal(0x0483, match.VendorId);
        Assert.Equal(0x5741, match.ProductId);
        Assert.Equal(expected.Value.SerialNumber, match.SerialNumber);
    }

    private static (string PortName, string SerialNumber)? ConnectedPort(string devicePath, HashSet<string> livePorts)
    {
        using var device = Registry.LocalMachine.OpenSubKey(devicePath);
        if (device == null)
        {
            return null;
        }

        foreach (var instanceId in device.GetSubKeyNames())
        {
            using var instance = device.OpenSubKey(instanceId);
            using var parameters = instance?.OpenSubKey("Device Parameters");
            var portName = parameters?.GetValue("PortName") as string;
            if (string.IsNullOrWhiteSpace(portName) || !livePorts.Contains(portName.Trim()))
            {
                continue;
            }

            return (portName.Trim(), instanceId);
        }

        return null;
    }
}
