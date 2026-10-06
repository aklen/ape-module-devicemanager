# Device Manager

**Ape.Module.DeviceManager** is an optional Ape module: it watches USB HID and serial ports, opens them, and can publish `Device` replicas on the shared scene so other peers see the same hardware metadata and payload.

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

Enable it in `workspace.yaml` under `modules:` (`name: Ape.Module.DeviceManager`, this GitHub URL). Host JSON turns the service and plugins on:

```json
"Ape.Module.DeviceManager": {
  "services": { "DeviceManagerService": {} },
  "plugins": { "DeviceExample": {} }
}
```

Plugins resolve `IDeviceManager` from DI. Subscribe with filters (type, VID/PID, path). Open serial with `SerialPortOptions` parsed from host JSON (`baudRate`, `parity`, …) — hardware-specific line settings belong in the product host file, not in this module.

`DeviceExample` is a two-role sample: the server owns the port and writes `Device` replicas; a client can observe `DeviceData` over the network.

Line settings, USB filters, and which plugin to load are configuration. The module does not hard-code a product board.

## License

Copyright (c) 2026 [Akos Hamori](https://github.com/aklen).

Licensed under the [Mozilla Public License 2.0 (MPL-2.0)](https://www.mozilla.org/MPL/2.0/).
