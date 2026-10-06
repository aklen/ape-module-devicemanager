# Device Manager

**Ape.Module.DeviceManager** is an optional Ape module: when host JSON names a device, it watches that port, opens it, and can publish a `Device` replica on the shared scene so other peers see the same hardware metadata and payload. With no device target it connects to nothing and registers nothing.

It is not part of Core. Core stays the scene, replica, and plugin host. This module is a vertical you add when a process needs real devices.

## Place in the ecosystem

| Piece | Role |
| --- | --- |
| [ape-skeleton](https://github.com/aklen/ape-skeleton) | Workspace vessel (`./ape sync`, `./ape build`) |
| [ape-core](https://github.com/aklen/ape-core) | Framework: scene, DI, plugins, commits |
| [ape-launcher](https://github.com/aklen/ape-launcher) | Host executable |
| **this repo** | Device stack + sample plugin |

After `./ape sync` the tree looks like:

```
ape-skeleton/
└── src/
    ├── Ape.Core/
    ├── Ape.Launcher/
    └── Ape.Modules/
        └── Ape.Module.DeviceManager/   ← this repository
```

Enable it in `workspace.yaml` under `modules:` (`name: Ape.Module.DeviceManager`, this GitHub URL). Host JSON turns the service and plugins on. Run from the skeleton:

```bash
./ape run -c src/Ape.Modules/Ape.Module.DeviceManager/Samples/device-example.json
```

```json
"Ape.Module.DeviceManager": {
  "services": {
    "DeviceManagerService": {
      "devices": [
        {
          "type": "serial",
          "vendorId": "0x0403",
          "productId": "0x6010",
          "portIndex": 0,
          "description": "lab serial adapter"
        },
        {
          "type": "hid",
          "vendorId": "0x046d",
          "productId": "0xc52b"
        },
        {
          "type": "hid",
          "path": "/dev/hidraw0"
        }
      ]
    }
  },
  "plugins": { "DeviceExample": {} }
}
```

`DeviceManagerService` with an empty object, or a `devices` list with no usable entry, names no device. The sample then does not open a port or create a `Device` replica. Every entry needs `type` (`serial` or `hid`) and either `vendorId` + `productId` or a `path`. Optional `description` is a label for that entry. It does not change which device matches. When set, it becomes the device's friendly name.

Plugins resolve `IDeviceManager` from DI. Open serial with `SerialPortOptions` parsed from host JSON (`baudRate`, `parity`, …) — hardware-specific line settings belong in the product host file, not in this module.

`DeviceExample` is a two-role sample: the server owns the configured port and writes `Device` replicas; a client can observe `DeviceData` over the network.

Line settings, USB filters, and which plugin to load are configuration. The module does not hard-code a product board.

## License

Copyright (c) 2026 [Akos Hamori](https://github.com/aklen).

Licensed under the [Mozilla Public License 2.0 (MPL-2.0)](https://www.mozilla.org/MPL/2.0/).
