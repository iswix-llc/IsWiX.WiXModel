using System;
using System.Linq;
using IsWiX.WiXModel.Core;
using Xunit;
using static IsWiX.WiXModel.Tests.TestText;

namespace IsWiX.WiXModel.Tests;

/// <summary>Bundles: the Burn elements, and the extension searches that only go in a bundle.</summary>
public class BundleTests
{
    private const string Source = """
        <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs" xmlns:util="http://wixtoolset.org/schemas/v4/wxs/util">
          <Bundle Name="Acme" Manufacturer="Acme" Version="1.0.0.0" UpgradeCode="6f330b47-2577-43ad-9095-1861ba25889b">
            <util:RegistrySearch Id="FindVersion" Variable="InstalledVersion" Root="HKLM" Key="Software\Acme" Value="Version" Bitness="always64" />
            <Variable Name="InstallFolder" Value="[ProgramFiles64Folder]Acme" bal:Overridable="yes" xmlns:bal="http://wixtoolset.org/schemas/v4/wxs/bal" />
            <Chain>
              <MsiPackage SourceFile="Acme.msi" Vital="yes" />
              <PackageGroupRef Id="Prereqs" />
            </Chain>
          </Bundle>
        </Wix>
        """;

    [Fact]
    public void Bundle_elements_load_as_typed_wrappers()
    {
        var document = Parse(Source);

        var bundle = document.Root.Bundles.Single();
        Assert.Equal("Acme", bundle.Name);
        Assert.Equal("InstallFolder", bundle.Variables.Single().Name);
        var chain = bundle.Chains.Single();
        Assert.Equal("Acme.msi", chain.MsiPackages.Single().SourceFile);
        Assert.True(chain.MsiPackages.Single().Vital!.Value.Value);
        Assert.Equal("Prereqs", chain.PackageGroupRefs.Single().Id);

        var search = bundle.UtilRegistrySearches.Single();
        Assert.Equal(BitnessType.Always64, search.Bitness!.Value.Value);
        Assert.Equal(Util.RegistrySearchRoot.HKLM, search.Root!.Value.Value);
        Assert.Empty(document.Validate());
    }

    [Fact]
    public void Extension_searches_can_be_added_to_a_bundle()
    {
        var document = Parse(Source);
        var bundle = document.Root.Bundles.Single();

        bundle.Add(new NetFx.DotNetCoreSearch { Id = "Runtime", Variable = "RuntimeVersion", RuntimeType = NetFx.DotNetCoreSearchRuntimeType.Desktop, Platform = NetFx.NetfxPlatformType.X64, MajorVersion = 8 });
        bundle.Chains.Single().Add(new ExePackage { Id = "Prereq", SourceFile = "prereq.exe", Permanent = true });

        var text = document.ToString();
        Assert.Contains("xmlns:netfx=\"http://wixtoolset.org/schemas/v4/wxs/netfx\"", text, StringComparison.Ordinal);
        Assert.Contains("<netfx:DotNetCoreSearch Id=\"Runtime\" Variable=\"RuntimeVersion\" RuntimeType=\"desktop\" Platform=\"x64\" MajorVersion=\"8\" />", text, StringComparison.Ordinal);
        Assert.Contains("      <ExePackage Id=\"Prereq\" SourceFile=\"prereq.exe\" Permanent=\"yes\" />\n", text, StringComparison.Ordinal);
        Assert.Single(bundle.NetFxDotNetCoreSearches);
    }

    [Fact]
    public void Chain_packages_share_one_id_table()
    {
        var document = Parse("""
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
              <Fragment>
                <PackageGroup Id="Prereqs">
                  <ExePackage Id="Runtime" SourceFile="runtime.exe" />
                  <MsiPackage Id="Runtime" SourceFile="runtime.msi" />
                </PackageGroup>
                <PayloadGroupRef Id="Missing" />
              </Fragment>
            </Wix>
            """);

        var diagnostics = WixDiagnostics.Validate(document);

        Assert.Equal([WixDiagnostics.DuplicateId, WixDiagnostics.UnresolvedReference], diagnostics.Select(d => d.Code));
        Assert.Equal("MsiPackage", diagnostics[0].Element.Name.LocalName);
    }
}
