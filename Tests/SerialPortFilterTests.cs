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

    [Fact]
    public void ParseUsbSerialCallouts_pairs_cdc_modem_with_parent_ids()
    {
        const string sample = """
            +-o USB2.1 Hub@00100000  <class IOUSBHostDevice, id 0x1>
            |   "idProduct" = 1120
            |   "USB Product Name" = "USB2.1 Hub"
            |   "idVendor" = 6309
            +-o ACM adapter@01120000  <class IOUSBHostDevice, id 0x2>
            |   "idProduct" = 22136
            |   "USB Product Name" = "ACM serial adapter"
            |   "idVendor" = 4660
            |   +-o IOSerialBSDClient  <class IOSerialBSDClient, id 0x3>
            |       "IOCalloutDevice" = "/dev/cu.usbmodem11201"
            """;

        var ports = MacSerialPortEnumerator.ParseUsbSerialCallouts(sample);

        Assert.Single(ports);
        Assert.Equal("/dev/cu.usbmodem11201", ports[0].CalloutPath);
        Assert.Equal(0x1234, ports[0].VendorId);
        Assert.Equal(0x5678, ports[0].ProductId);
        Assert.Equal("ACM serial adapter", ports[0].ProductName);
        Assert.Equal(0, ports[0].PortIndex);
    }

    [Fact]
    public void ParseUsbSerialCallouts_numbers_two_ports_on_one_device()
    {
        const string sample = """
            +-o Dual RS232-HS@00120000  <class IOUSBHostDevice, id 0x1>
            |   "idProduct" = 24592
            |   "idVendor" = 1027
            |   "USB Product Name" = "Dual RS232-HS"
            |   "USB Serial Number" = "ABCD1234"
            |   "IOCalloutDevice" = "/dev/cu.usbserial-1201"
            |   "IOCalloutDevice" = "/dev/cu.usbserial-1200"
            """;

        var ports = MacSerialPortEnumerator.ParseUsbSerialCallouts(sample);

        Assert.Equal(2, ports.Count);
        Assert.Equal(0, ports[0].PortIndex);
        Assert.Equal(1, ports[1].PortIndex);
        Assert.Equal(0x0403, ports[0].VendorId);
        Assert.Equal("ABCD1234", ports[1].SerialNumber);
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
        manager.SetConnectionFilter(filter);
        var matches = manager.QuerySerialPorts(filter);

        Assert.Single(matches);
        Assert.Equal("/dev/cu.usbserial-1200", matches[0].DevicePath);
    }

    [Fact]
    public void QuerySerialPorts_returns_empty_when_config_names_no_device()
    {
        var enumerator = new SerialPortEnumerator.PassthroughSerialPortEnumerator(
        [
            new SerialPortInfo("/dev/cu.Bluetooth-Incoming-Port"),
            new SerialPortInfo("/dev/cu.usbserial-1200", 0x0403, 0x6010, portIndex: 0),
        ]);

        var manager = new Services.DeviceManagement.DeviceManager(null!, null!, enumerator);
        var matches = manager.QuerySerialPorts(DeviceFilter.ByType(DeviceType.Serial));

        Assert.Empty(matches);
    }

    [Fact]
    public void QuerySerialPorts_returns_only_the_configured_path()
    {
        var enumerator = new SerialPortEnumerator.PassthroughSerialPortEnumerator(
        [
            new SerialPortInfo("/dev/cu.Bluetooth-Incoming-Port"),
            new SerialPortInfo("/dev/cu.usbserial-1200", 0x0403, 0x6010, portIndex: 0),
        ]);

        var manager = new Services.DeviceManagement.DeviceManager(null!, null!, enumerator);
        manager.SetConnectionFilter(DeviceFilter.ByPath("/dev/cu.usbserial-1200"));
        var matches = manager.QuerySerialPorts(DeviceFilter.ByType(DeviceType.Serial));

        Assert.Single(matches);
        Assert.Equal("/dev/cu.usbserial-1200", matches[0].DevicePath);
    }
}

public class DeviceConnectionConfigTests
{
    [Fact]
    public void TryRead_returns_null_for_empty_service()
    {
        Assert.Null(DeviceConnectionConfig.TryRead(null));
        Assert.Null(DeviceConnectionConfig.TryRead(new Ape.Core.Config.Models.ConfigNode()));
    }

    [Fact]
    public void TryRead_matches_vendor_product_from_serial_entry()
    {
        var service = new Ape.Core.Config.Models.ConfigNode();
        var serial = new Ape.Core.Config.Models.ConfigNode();
        serial.SetString("type", "serial");
        serial.SetString("vendorId", "0x0403");
        serial.SetString("productId", "0x6010");
        serial.SetInt("portIndex", 0);
        service.PushArrayElement("devices", serial);

        var filter = DeviceConnectionConfig.TryRead(service);
        Assert.NotNull(filter);

        var match = SerialPortDeviceFactory.ToDeviceInternal(
            new SerialPortInfo("/dev/cu.usbserial-1200", 0x0403, 0x6010, portIndex: 0));
        var other = SerialPortDeviceFactory.ToDeviceInternal(
            new SerialPortInfo("/dev/cu.Bluetooth-Incoming-Port"));

        Assert.True(filter!.Predicate(match));
        Assert.False(filter.Predicate(other));
    }

    [Fact]
    public void TryRead_matches_path_entry()
    {
        var service = new Ape.Core.Config.Models.ConfigNode();
        var entry = new Ape.Core.Config.Models.ConfigNode();
        entry.SetString("type", "serial");
        entry.SetString("path", "/dev/cu.usbserial-1200");
        service.PushArrayElement("devices", entry);

        var filter = DeviceConnectionConfig.TryRead(service);
        Assert.NotNull(filter);

        var match = SerialPortDeviceFactory.ToDeviceInternal(
            new SerialPortInfo("/dev/cu.usbserial-1200"));
        var other = SerialPortDeviceFactory.ToDeviceInternal(
            new SerialPortInfo("/dev/cu.JBLCharge4"));

        Assert.True(filter!.Predicate(match));
        Assert.False(filter.Predicate(other));
    }

    [Fact]
    public void TryRead_matches_hid_vendor_product()
    {
        var service = new Ape.Core.Config.Models.ConfigNode();
        var hid = new Ape.Core.Config.Models.ConfigNode();
        hid.SetString("type", "hid");
        hid.SetString("vendorId", "0x046D");
        hid.SetString("productId", "0xC52B");
        service.PushArrayElement("devices", hid);

        var filter = DeviceConnectionConfig.TryRead(service);
        Assert.NotNull(filter);

        var match = new DeviceInternal
        {
            DeviceType = DeviceType.HID,
            VendorId = 0x046D,
            ProductId = 0xC52B,
            DevicePath = "hid:matching",
        };
        var sameIdsOnSerial = SerialPortDeviceFactory.ToDeviceInternal(
            new SerialPortInfo("/dev/cu.usbserial-1200", 0x046D, 0xC52B));

        Assert.True(filter!.Predicate(match));
        Assert.False(filter.Predicate(sameIdsOnSerial));
    }

    [Fact]
    public void TryRead_matches_hid_path_in_devices_list()
    {
        var service = new Ape.Core.Config.Models.ConfigNode();
        var entry = new Ape.Core.Config.Models.ConfigNode();
        entry.SetString("type", "hid");
        entry.SetString("path", "hid:gamepad");
        service.PushArrayElement("devices", entry);

        var filter = DeviceConnectionConfig.TryRead(service);
        Assert.NotNull(filter);

        var match = new DeviceInternal { DeviceType = DeviceType.HID, DevicePath = "hid:gamepad" };
        var other = new DeviceInternal { DeviceType = DeviceType.HID, DevicePath = "hid:other" };

        Assert.True(filter!.Predicate(match));
        Assert.False(filter.Predicate(other));
    }

    [Fact]
    public void TryRead_ignores_unsupported_device_type()
    {
        var service = new Ape.Core.Config.Models.ConfigNode();
        var entry = new Ape.Core.Config.Models.ConfigNode();
        entry.SetString("type", "bluetooth");
        entry.SetString("path", "/dev/cu.JBLCharge4");
        service.PushArrayElement("devices", entry);

        Assert.Null(DeviceConnectionConfig.TryRead(service));
    }

    [Fact]
    public void TryRead_ignores_an_entry_without_type()
    {
        var service = new Ape.Core.Config.Models.ConfigNode();
        var entry = new Ape.Core.Config.Models.ConfigNode();
        entry.SetString("vendorId", "0x0403");
        entry.SetString("productId", "0x6010");
        service.PushArrayElement("devices", entry);

        Assert.Null(DeviceConnectionConfig.TryRead(service));
    }

    [Fact]
    public void Description_labels_the_match_and_does_not_filter()
    {
        var service = new Ape.Core.Config.Models.ConfigNode();
        var entry = new Ape.Core.Config.Models.ConfigNode();
        entry.SetString("type", "serial");
        entry.SetString("vendorId", "0x0403");
        entry.SetString("productId", "0x6010");
        entry.SetString("description", "bench adapter");
        service.PushArrayElement("devices", entry);

        var targets = DeviceConnectionConfig.ReadTargets(service);
        Assert.Single(targets);
        Assert.Equal("bench adapter", targets[0].Description);

        var enumerator = new SerialPortEnumerator.PassthroughSerialPortEnumerator(
        [
            new SerialPortInfo("/dev/cu.usbserial-1200", 0x0403, 0x6010, portIndex: 0),
            new SerialPortInfo("/dev/cu.other", 0x1111, 0x2222, portIndex: 0),
        ]);
        var manager = new Services.DeviceManagement.DeviceManager(null!, null!, enumerator);
        manager.SetConnectionTargets(targets);

        var matches = manager.QuerySerialPorts(DeviceFilter.ByType(DeviceType.Serial));
        Assert.Single(matches);
        Assert.Equal("/dev/cu.usbserial-1200", matches[0].DevicePath);
        Assert.Equal("bench adapter", matches[0].FriendlyName);
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
