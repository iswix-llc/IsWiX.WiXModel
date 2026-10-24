# IsWiX.WiXModel

A typed object model for WiX v4+ source (`.wxs`) and include (`.wxi`) files, generated from the WiX schema
and backed directly by the XML, so editing a hand-written file changes only what you edit.

```csharp
using IsWiX.WiXModel;

var document = WixDocument.Load("Package.wxs");
var package = document.Root.Packages.Single();

package.Version = "2.0.0.0";                      // only this attribute's text changes
var component = package.Directories.Single().Add(new Component { Bitness = BitnessType.Always64 });
component.Add(new File { Source = "App.exe", KeyPath = true });   // indented like its siblings

document.Save("Package.wxs");
```

## Source and include files

| Class | File | Root |
|---|---|---|
| `WixDocument` | `.wxs` source | `Root` is a typed `Wix` |
| `WixIncludeDocument` | `.wxi` include | `Root` is a typed `Include` |
| `WixSourceDocument` | either | the base class; `WixSourceDocument.Load` returns whichever one the file is |

The schema lets `<Include>` hold any element, so its contents are read through `Children`
(`include.Root.Children.OfType<Property>()`) and added with `include.Root.Add(...)`. `<?define?>`, `<?if?>`
and other preprocessor instructions are kept exactly as written. Loading a file with the wrong class fails
with a message naming the right one.

Either kind of file exposes its `<?define?>` instructions as `document.Defines`:

```csharp
var sourceDir = document.Defines["SourceDir"];      // null when not defined
document.Defines["SourceDir"] = @"..\Deploy";        // changes only the value, or adds <?define SourceDir="..\Deploy"?>
foreach (var define in document.Defines) { /* Name, Value, IsConditional */ }
```

A new define goes after the last unconditional one (or first in the root), on its own line. A name defined
only inside `<?if?>` or `<?foreach?>` blocks is not changed: which branch is meant is ambiguous.

## Diagnostics

`document.Validate()` finds common mistakes without building, and never changes the document. Each
`WixDiagnostic` has a code, severity, message, element, attribute, and line and column in the loaded file:

| Code | Severity | Problem |
|---|---|---|
| `WXM001` | error | Duplicate Id among elements that share a table (two `Property Id="X"`, two `Component Id="Y"`, ...) |
| `WXM002` | error | Attribute text that is neither a valid value nor an expression (`KeyPath="maybe"`), with the allowed values |
| `WXM003` | warning | A `ComponentGroupRef`, `ComponentRef`, `FeatureRef`, `FeatureGroupRef` or `MergeRef` nothing defines |

`WixDiagnostics.Validate(product, files, ...)` checks a build's files together: duplicates across them and
unresolved references, which are warnings since a .wixlib or extension may define them. Elements inside
`<?if?>`/`<?foreach?>` blocks, Ids that are expressions, and `virtual`/`override` Ids are not reported as
duplicates; `StandardDirectory` may repeat.

## Canonical order

For files that are branched and merged, an element's position should depend only on the file's content, not
on the order edits happened in. Set an ordering on the document and the model inserts every new element at
its canonical position:

```csharp
var document = WixDocument.Load("Product.wxs");
document.Ordering = CanonicalOrder.Create();   // IsWiX's conventions
document.Canonicalize();                        // optional: re-sort what is already there
package.Add(new Property { Id = "ARPNOMODIFY", Value = "1" });   // lands in Id order among the properties
```

- `CanonicalOrder.Create()` is IsWiX's order: `Package` and `Fragment` children by kind (summary information
  and upgrade first, sequences last), `Property` and top-level `Feature` by `Id`, `Component` in a
  `ComponentGroup` by file source, `File` by source. Kinds it does not list go last, in their existing order.
- In a file that is not yet in canonical order, a new element still goes beside the existing elements of its
  kind, in key order among them, rather than where the kind order alone would put it.
- Rules are data on a `WixOrdering` (`OrderChildren`, `SortBy`), so they can be extended or replaced.
- `Canonicalize()` moves each element together with the comments directly above it, leaves an already
  canonical document byte-for-byte unchanged, and never reorders a parent containing `<?if?>` or
  `<?foreach?>` (new elements go after such blocks instead).
- Without an ordering (the default), new elements go after the last sibling of the same kind.

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
  expression, or invalid text kept as written. Reading never throws or rewrites. Editors convert text with
  `WixConverters` (yes/no, numbers) and `WixEnumConverters` (each enum, e.g. listing its choices).
- **Extension namespaces declare themselves.** Adding an extension element (`element.AddElement(new XElement(WixNamespaces.Extension("util") + "User"))`)
  or setting an extension attribute declares its namespace on the root with WiX's usual prefix (`util`,
  `netfx`, `fw`, ...), or keeps the prefix the file already uses, instead of writing a made-up `p1:`.
- **No global state, no UI.** `netstandard2.0` and `net8.0`; WPF and designers live in IsWiX.
- **WiX v4+ only.** v3 files are rejected with a hint to run `wix convert`.
- **Exact WiX names.** Classes, properties and enum values use the names in the WiX schema and docs, even
  where they match a .NET type (see below).

## Names that match .NET types

Element classes keep their exact WiX names, so code reads like the `.wxs` it edits. Thirteen of them share
a name with a type in a namespace .NET projects commonly import, most of them in UI frameworks:

| WiX class | Same name as | Usually imported by |
|---|---|---|
| `Environment` | `System.Environment` | `using System;` (every project) |
| `File`, `Directory` | `System.IO.File`, `System.IO.Directory` | `using System.IO;` (SDK implicit usings) |
| `Module`, `AssemblyName` | `System.Reflection.Module`, `System.Reflection.AssemblyName` | `using System.Reflection;` |
| `Icon` | `System.Drawing.Icon` | WinForms implicit usings |
| `Shortcut` | `System.Windows.Forms.Shortcut` | WinForms implicit usings |
| `Control`, `ComboBox`, `ListBox`, `ListView`, `RadioButton` | the WinForms and WPF controls of the same name | `System.Windows.Forms` (WinForms implicit usings), `System.Windows.Controls` |
| `ListItem` | `System.Windows.Documents.ListItem` | `System.Windows.Documents` (WPF) |

When a file imports both namespaces, the short name is ambiguous (error CS0104). Resolve it with using
aliases, either at the top of the file or once per project in a `GlobalUsings.cs`:

```csharp
using IsWiX.WiXModel;
using Environment = IsWiX.WiXModel.Environment;   // the WiX element in this file
using File = IsWiX.WiXModel.File;
using SysEnvironment = System.Environment;        // and a short name for the .NET type
using IOFile = System.IO.File;
```

or by writing the .NET type out in full (`System.Environment.GetEnvironmentVariable("PATH")`).

In WinForms or WPF code that uses many of these, alias the namespace instead of each type, so WiX elements
read as `Wxs.Control` next to the framework's `Control`:

```csharp
using System.Windows.Controls;
using Wxs = IsWiX.WiXModel;

var dialog = document.Root.Fragments.Single().UIs.Single().Dialogs.Single();
foreach (Wxs.Control control in dialog.Controls)
{
    var button = new Button { Content = control.Text };   // a WPF control
}
```

Code in a namespace under `IsWiX.WiXModel` (such as `IsWiX.WiXModel.Tests`) gets no error: the WiX type
silently wins, so the .NET type must always be qualified or aliased there.

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
separated by `;`. Every WiX v4+ `.wxs` and `.wxi` under them must save back byte-for-byte, and at least
95% of the files found must load (WiX's own tests deliberately include a few v3 and malformed files).

## Packages and releases

Two NuGet packages ship from this repository with the same version: `IsWiX.WiXModel.Core` and
`IsWiX.WiXModel` (which depends on it). The version lives in `src/Directory.Build.props`.

- **CI** (`.github/workflows/ci.yml`) builds, tests and packs every pull request to `main`.
- **Corpus** (`.github/workflows/corpus.yml`) runs the tests on every pull request with `WIXMODEL_CORPUS` set
  to a sparse clone of [wixtoolset/wix](https://github.com/wixtoolset/wix) (only its `.wxs` and `.wxi`
  files) at the tag in `schemaVersion` of `src/IsWiX.WiXModel/schemagen.json`. Run it by hand to test
  against another tag or branch.
- **Publish** (`.github/workflows/publish.yml`) is run by hand from the Actions tab: choose `patch`,
  `minor` or `major`. It increments the version, builds and tests, signs the assemblies with Azure
  Artifact Signing, packs, pushes both packages (with symbols) to nuget.org using trusted publishing,
  then commits `Release x.y.z` to `main` and tags it `vx.y.z`.

The publish workflow needs the `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` and
`NUGET_USER` repository secrets, an Azure federated credential for this repository's `main` branch, and a
nuget.org trusted publishing policy for this repository and `publish.yml`.

## License

[MIT](LICENSE)
