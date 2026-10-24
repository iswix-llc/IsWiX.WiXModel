using System;
using System.Linq;
using IsWiX.WiXModel.Core;
using Xunit;
using static IsWiX.WiXModel.Tests.TestText;

namespace IsWiX.WiXModel.Tests;

/// <summary>The typed WiX NetFx and UI extension elements, generated like the Util ones.</summary>
public class NetFxAndUIExtensionTests
{
    private const string Source = """
        <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
          <Package Name="Acme" Manufacturer="Acme" Version="1.0.0.0" UpgradeCode="6f330b47-2577-43ad-9095-1861ba25889b">
            <Component Id="App" Directory="INSTALLFOLDER">
              <File Id="AppExe" Source="app.exe" />
            </Component>
          </Package>
        </Wix>
        """;

    [Fact]
    public void Adding_netfx_and_ui_elements_declares_their_prefixes()
    {
        var document = Parse(Source);
        var package = document.Root.Packages.Single();
        var file = package.Components.Single().Files.Single();

        file.Add(new NetFx.NativeImage { Id = "AppNgen", Platform = NetFx.NativeImagePlatformType.All, Dependencies = false });
        package.Add(new UIExtension.WixUI { Id = "WixUI_InstallDir", InstallDirectory = "INSTALLFOLDER" });

        Assert.Equal(Lf("""
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs" xmlns:netfx="http://wixtoolset.org/schemas/v4/wxs/netfx" xmlns:ui="http://wixtoolset.org/schemas/v4/wxs/ui">
              <Package Name="Acme" Manufacturer="Acme" Version="1.0.0.0" UpgradeCode="6f330b47-2577-43ad-9095-1861ba25889b">
                <Component Id="App" Directory="INSTALLFOLDER">
                  <File Id="AppExe" Source="app.exe">
                    <netfx:NativeImage Id="AppNgen" Platform="all" Dependencies="no" />
                  </File>
                </Component>
                <ui:WixUI Id="WixUI_InstallDir" InstallDirectory="INSTALLFOLDER" />
              </Package>
            </Wix>
            """), document.ToString());
    }

    [Fact]
    public void Netfx_and_ui_elements_load_as_typed_wrappers()
    {
        var document = Parse("""
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs" xmlns:netfx="http://wixtoolset.org/schemas/v4/wxs/netfx" xmlns:ui="http://wixtoolset.org/schemas/v4/wxs/ui">
              <Fragment>
                <netfx:DotNetCoreSearch Id="Runtime" Variable="RuntimeVersion" RuntimeType="desktop" Platform="x64" MajorVersion="8" />
                <ui:WixUI Id="WixUI_Minimal" ExtendedPathValidation="$(var.Validate)" />
                <UI>
                  <ui:WixUI Id="WixUI_Common" />
                </UI>
              </Fragment>
            </Wix>
            """);
        var fragment = document.Root.Fragments.Single();

        var search = fragment.NetFxDotNetCoreSearches.Single();
        Assert.Equal(NetFx.DotNetCoreSearchRuntimeType.Desktop, search.RuntimeType!.Value.Value);
        Assert.Equal(8, search.MajorVersion!.Value.Value);
        Assert.Equal("$(var.Validate)", fragment.WixUIs.Single().ExtendedPathValidation!.Value.Text);
        Assert.Equal("WixUI_Common", fragment.UIs.Single().WixUIs.Single().Id);
        Assert.Empty(document.Validate());
    }

    [Fact]
    public void Util_elements_without_content_get_accessors_on_their_parents()
    {
        Assert.NotNull(typeof(UI).GetProperty("UtilBroadcastEnvironmentChanges"));
        Assert.NotNull(typeof(Package).GetProperty("UtilQueryWindowsWellKnownSIDsElements"));
    }

    [Theory]
    [InlineData(typeof(NetFx.NativeImage), nameof(NetFx.NativeImage.Profile), typeof(WixValue<bool>?))]
    [InlineData(typeof(NetFx.DotNetCoreSearch), nameof(NetFx.DotNetCoreSearch.MajorVersion), typeof(WixValue<int>?))]
    [InlineData(typeof(NetFx.DotNetCoreSearch), nameof(NetFx.DotNetCoreSearch.Platform), typeof(WixValue<NetFx.NetfxPlatformType>?))]
    [InlineData(typeof(UIExtension.WixUI), nameof(UIExtension.WixUI.InstallDirectory), typeof(string))]
    public void Extension_types_map_to_csharp_types(Type element, string property, Type expected)
    {
        Assert.Equal(expected, element.GetProperty(property)!.PropertyType);
    }
}
