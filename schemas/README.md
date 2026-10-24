# Vendored WiX schema

`wix/` holds the per-element schema fragments for the core WiX namespace
(`http://wixtoolset.org/schemas/v4/wxs`), copied unmodified from
[wixtoolset/wix `src/xsd/wix` at tag `v7.0.0`](https://github.com/wixtoolset/wix/tree/v7.0.0/src/xsd/wix).

The WiX build stitches these fragments into a single `wix.xsd`; `IsWiX.WiXModel.SchemaGen`
reads them directly, so no stitching step is needed here.

To move to a newer WiX release, replace the folder with the fragments from the new tag,
update `schemaVersion` in `src/IsWiX.WiXModel/schemagen.json`, and regenerate (see the root README).
