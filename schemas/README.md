# Vendored WiX schema

`wix/` holds the per-element schema fragments for the core WiX namespace
(`http://wixtoolset.org/schemas/v4/wxs`), copied unmodified from
[wixtoolset/wix `src/xsd/wix` at tag `v7.0.0`](https://github.com/wixtoolset/wix/tree/v7.0.0/src/xsd/wix).

The extension folders hold the fragments of WiX extensions, from the same tag:

| Folder | Namespace | Source |
|---|---|---|
| `util/` | `http://wixtoolset.org/schemas/v4/wxs/util` | [`src/xsd/util`](https://github.com/wixtoolset/wix/tree/v7.0.0/src/xsd/util) |
| `netfx/` | `http://wixtoolset.org/schemas/v4/wxs/netfx` | [`src/xsd/netfx`](https://github.com/wixtoolset/wix/tree/v7.0.0/src/xsd/netfx) |
| `ui/` | `http://wixtoolset.org/schemas/v4/wxs/ui` | [`src/xsd/ui`](https://github.com/wixtoolset/wix/tree/v7.0.0/src/xsd/ui) |

The WiX build stitches these fragments into a single `.xsd` per namespace; `IsWiX.WiXModel.SchemaGen`
reads them directly, so no stitching step is needed here.

To move to a newer WiX release, replace the folders with the fragments from the new tag,
update `schemaVersion` in `src/IsWiX.WiXModel/schemagen.json` and each `schemagen.<extension>.json`, and
regenerate (see the root README).
