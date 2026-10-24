# IsWiX.WiXModel

A typed object model for WiX v4+ source (`.wxs`) files, generated from the WiX schema and backed
directly by the XML, so editing a hand-written file changes only what you edit.

```csharp
using IsWiX.WiXModel;

var document = WixDocument.Load("Package.wxs");
var package = document.Root.Packages.Single();

package.Version = "2.0.0.0";                      // only this attribute's text changes
var component = package.Directories.Single().Add(new Component { Bitness = BitnessType.Always64 });
component.Add(new File { Source = "App.exe", KeyPath = true });   // indented like its siblings

document.Save("Package.wxs");
```

## Layout

| Path | What it is |
|---|---|
| `src/IsWiX.WiXModel.Core` | Infrastructure: lossless XML load/save (`LosslessXml`), `WixValue<T>` and converters, the `WixElement` base class and wrapper registry. No WiX element types. |
| `src/IsWiX.WiXModel` | `WixDocument` plus the generated element wrappers in `Generated/` (checked in, never hand-edited). |
| `tools/IsWiX.WiXModel.SchemaGen` | Reads the WiX schema fragments and emits `src/IsWiX.WiXModel/Generated`. |
| `schemas/wix` | The WiX core schema, vendored from the `v7.0.0` tag (see `schemas/README.md`). |
| `tests/IsWiX.WiXModel.Tests` | Round-trip, editing and generator tests. |

## Design rules

- **The XML is the only state.** Wrappers read and write their `XElement`; nothing is cached, so
  comments, `<?define?>`/`<?if?>`, extension elements and attributes all survive.
- **Unchanged means unchanged.** Loading captures each node's source text; saving reuses it for any node
  whose value hasn't changed, down to individual attributes. Encoding, BOM, line endings and indentation
  are detected and used for new content.
- **Values can be expressions.** Typed attributes are `WixValue<T>?`: a literal, a `$(var.X)`/`!(loc.X)`
  expression, or invalid text kept as written. Reading never throws or rewrites.
- **No global state, no UI.** `netstandard2.0` and `net8.0`; WPF and designers live in IsWiX.
- **WiX v4+ only.** v3 files are rejected with a hint to run `wix convert`.

## Regenerating the model

Add element names to `src/IsWiX.WiXModel/schemagen.json`, then:

```bash
dotnet run --project tools/IsWiX.WiXModel.SchemaGen -- src/IsWiX.WiXModel/schemagen.json
```

`Generated_code_is_up_to_date` fails if the checked-in output doesn't match the generator.

## Testing

```bash
dotnet test
```

To run the round-trip tests over a larger corpus (for example a clone of
[wixtoolset/wix](https://github.com/wixtoolset/wix)), point `WIXMODEL_CORPUS` at one or more folders,
separated by `;`. Every WiX v4+ `.wxs` under them must save back byte-for-byte.

## Packages and releases

Two NuGet packages ship from this repository with the same version: `IsWiX.WiXModel.Core` and
`IsWiX.WiXModel` (which depends on it). The version lives in `src/Directory.Build.props`.

- **CI** (`.github/workflows/ci.yml`) builds, tests and packs every pull request to `main`.
- **Publish** (`.github/workflows/publish.yml`) is run by hand from the Actions tab: choose `patch`,
  `minor` or `major`. It increments the version, builds and tests, signs the assemblies with Azure
  Artifact Signing, packs, pushes both packages (with symbols) to nuget.org using trusted publishing,
  then commits `Release x.y.z` to `main` and tags it `vx.y.z`.

The publish workflow needs the `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` and
`NUGET_USER` repository secrets, an Azure federated credential for this repository's `main` branch, and a
nuget.org trusted publishing policy for this repository and `publish.yml`.

## License

[MIT](LICENSE)
