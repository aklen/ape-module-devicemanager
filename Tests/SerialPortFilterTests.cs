using Ape.Module.DeviceManager.SceneEntities.Models;
using Ape.Module.DeviceManager.Services.DeviceManagement;
using Ape.Module.DeviceManager.Services.DeviceManagement.Serial;

namespace Ape.Module.DeviceManager.Tests;

public class LinuxByIdParserTests
{
    [Theory]
    [InlineData("usb-FTDI_Dual_RS232-HS-if01-port0", 0)]
    [InlineData("usb-FTDI_Dual_RS232-HS-if01-port1", 1)]
    [InlineData("usb-FTDI_FT232R_USB_UART-if00-port0", 0)]
    public void ParsePortIndex_extracts_suffix(string byIdName, int expected)
    {
        Assert.Equal(expected, LinuxByIdParser.ParsePortIndex(byIdName));
    }

    [Fact]
    public void ParseProductName_extracts_ftdi_product()
    {
        Assert.Equal("FTDI Dual RS232-HS", LinuxByIdParser.ParseProductName("usb-FTDI_Dual_RS232-HS-if01-port0"));
    }
}

public class MacSerialPortEnumeratorTests
{
    [Fact]
    public void ParseUsbDevices_reads_ftdi_dual()
    {
        const string sample = """
            +-o Dual RS232-HS@00120000  <class IOUSBHostDevice>
            |       "idProduct" = 24592
            |       "idVendor" = 1027
            |       "USB Product Name" = "Dual RS232-HS"
            |       "USB Serial Number" = "ABCD1234"
            """;

        var devices = MacSerialPortEnumerator.ParseUsbDevices(sample);
        Assert.Single(devices);
        Assert.Equal(0x0403, devices[0].VendorId);
        Assert.Equal(0x6010, devices[0].ProductId);
        Assert.Equal("Dual RS232-HS", devices[0].ProductName);
        Assert.Equal("ABCD1234", devices[0].SerialNumber);
    }

    [Fact]
    public void ParseCalloutDevices_reads_cu_paths()
    {
        const string sample = """
              "IOCalloutDevice" = "/dev/cu.usbserial-1200"
              "IOCalloutDevice" = "/dev/cu.usbserial-1201"
            """;

        var paths = MacSerialPortEnumerator.ParseCalloutDevices(sample);
        Assert.Equal(2, paths.Count);
        Assert.Equal("/dev/cu.usbserial-1200", paths[0]);
    }
}

public class DeviceFilterSerialTests
{
    [Fact]
    public void ByPortIndex_matches_device_data()
    {
        var device = SerialPortDeviceFactory.ToDeviceInternal(
            new SerialPortInfo("/dev/cu.usbserial-1200", 0x0403, 0x6010, portIndex: 0));

        var filter = DeviceFilter.ByType(DeviceType.Serial)
            .And(DeviceFilter.ByVendorProduct(0x0403, 0x6010))
            .And(DeviceFilter.ByPortIndex(0));

        Assert.True(filter.Predicate(device));
        Assert.False(DeviceFilter.ByPortIndex(1).Predicate(device));
    }

    [Fact]
    public void QuerySerialPorts_uses_injected_enumerator()
    {
        var enumerator = new SerialPortEnumerator.PassthroughSerialPortEnumerator(
        [
            new SerialPortInfo("/dev/cu.usbserial-1200", 0x0403, 0x6010, portIndex: 0),
            new SerialPortInfo("/dev/cu.usbserial-1201", 0x0403, 0x6010, portIndex: 1),
        ]);

        var manager = new Services.DeviceManagement.DeviceManager(null!, null!, enumerator);
        var filter = DeviceFilter.ByVendorProduct(0x0403, 0x6010).And(DeviceFilter.ByPortIndex(0));
        var matches = manager.QuerySerialPorts(filter);

        Assert.Single(matches);
        Assert.Equal("/dev/cu.usbserial-1200", matches[0].DevicePath);
    }
}

public class SerialPortOptionsParseTests
{
    [Fact]
    public void Parse_reads_line_settings_from_node()
    {
        var node = new Ape.Core.Config.Models.ConfigNode();
        node.SetInt("baudRate", 5_000_000);
        node.SetString("parity", "Even");
        node.SetInt("dataBits", 8);
        node.SetString("stopBits", "One");

        var options = SerialPortOptions.Parse(node);

        Assert.Equal(5_000_000, options.BaudRate);
        Assert.Equal(System.IO.Ports.Parity.Even, options.Parity);
        Assert.Equal(8, options.DataBits);
        Assert.Equal(System.IO.Ports.StopBits.One, options.StopBits);
    }

    [Fact]
    public void Parse_uses_fallback_when_node_null()
    {
        var fallback = new SerialPortOptions { BaudRate = 115200 };
        Assert.Equal(115200, SerialPortOptions.Parse(null, fallback).BaudRate);
    }
}
