using System;
using System.Linq;
using Xunit;
using static IsWiX.WiXModel.Tests.TestText;

namespace IsWiX.WiXModel.Tests;

/// <summary>One-call helpers for common authoring patterns.</summary>
public class AuthoringTests
{
    private static WixDocument Canonical(string text)
    {
        var document = Parse(text);
        document.Ordering = CanonicalOrder.Create();
        return document;
    }

    [Fact]
    public void Files_folders_shortcuts_and_services()
    {
        var document = Canonical("""
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
              <Fragment>
                <ComponentGroup Id="Files" Directory="INSTALLFOLDER" />
              </Fragment>
            </Wix>
            """);
        var group = document.Root.Fragments.Single().ComponentGroups.Single();

        var app = group.AddFile(@"$(var.SourceDir)\app.exe");
        group.AddFile(@"$(var.SourceDir)\bin\tool.exe", "INSTALLFOLDER", "bin");
        group.AddFolder("INSTALLFOLDER", "logs", Guid.Parse("6b1e4a4e-8f1c-4c55-9a3e-2d4f8c1b7a10"));
        app.AddShortcut("Acme", "ProgramMenuFolder", "Acme");
        ((Component)app.Parent!).AddService("AcmeSvc", "Acme Service");

        Assert.Equal(Lf("""
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
              <Fragment>
                <ComponentGroup Id="Files" Directory="INSTALLFOLDER">
                  <Component Directory="INSTALLFOLDER" Subdirectory="logs" Guid="6b1e4a4e-8f1c-4c55-9a3e-2d4f8c1b7a10" KeyPath="yes">
                    <CreateFolder />
                  </Component>
                  <Component>
                    <File Source="$(var.SourceDir)\app.exe" KeyPath="yes">
                      <Shortcut Name="Acme" Directory="ProgramMenuFolder" Subdirectory="Acme" Advertise="yes" />
                    </File>
                    <ServiceInstall Name="AcmeSvc" DisplayName="Acme Service" ErrorControl="normal" Start="auto" Type="ownProcess" />
                    <ServiceControl Name="AcmeSvc" Start="install" Stop="both" Remove="both" Wait="yes" />
                  </Component>
                  <Component Directory="INSTALLFOLDER" Subdirectory="bin">
                    <File Source="$(var.SourceDir)\bin\tool.exe" KeyPath="yes" />
                  </Component>
                </ComponentGroup>
              </Fragment>
            </Wix>
            """), document.ToString());
        Assert.Empty(document.Validate());
    }

    [Fact]
    public void Feature_references_are_added_once()
    {
        var document = Canonical("""
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
              <Package Name="Sample" Manufacturer="Acme" Version="1.0.0.0">
                <Feature Id="Main">
                  <ComponentGroupRef Id="Files" />
                </Feature>
              </Package>
            </Wix>
            """);
        var feature = document.Root.Packages.Single().Features.Single();

        var existing = feature.AddComponentGroupRef("Files");
        feature.AddComponentGroupRef("Docs");
        feature.AddMergeRef("Runtime");
        feature.AddMergeRef("Runtime");

        Assert.Same(feature.ComponentGroupRefs.First(), existing);
        Assert.Equal(Lf("""
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
              <Package Name="Sample" Manufacturer="Acme" Version="1.0.0.0">
                <Feature Id="Main">
                  <ComponentGroupRef Id="Files" />
                  <ComponentGroupRef Id="Docs" />
                  <MergeRef Id="Runtime" />
                </Feature>
              </Package>
            </Wix>
            """), document.ToString());
    }

    [Fact]
    public void Setting_a_property_value_adds_or_changes_only_the_value()
    {
        var source = """
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
              <Package Name="Sample" Manufacturer="Acme" Version="1.0.0.0">
                <Property Id="BRAVO" Value="1" Secure="yes" />
              </Package>
            </Wix>
            """;
        var document = Canonical(source);
        var package = document.Root.Packages.Single();

        var bravo = package.SetPropertyValue("BRAVO", "2");
        package.SetPropertyValue("ALPHA", "$(var.Alpha)");
        Assert.Same(bravo, package.SetPropertyValue("BRAVO", "2"));

        Assert.Equal(Lf("""
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
              <Package Name="Sample" Manufacturer="Acme" Version="1.0.0.0">
                <Property Id="ALPHA" Value="$(var.Alpha)" />
                <Property Id="BRAVO" Value="2" Secure="yes" />
              </Package>
            </Wix>
            """), document.ToString());
    }

    [Fact]
    public void Helpers_check_their_arguments()
    {
        var document = Canonical("""
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
              <Fragment>
                <ComponentGroup Id="Files" />
              </Fragment>
            </Wix>
            """);
        var fragment = document.Root.Fragments.Single();
        var group = fragment.ComponentGroups.Single();

        Assert.Throws<ArgumentException>(() => group.AddFile(""));
        Assert.Throws<ArgumentException>(() => group.AddFolder(""));
        Assert.Throws<ArgumentException>(() => fragment.SetPropertyValue("", "x"));
    }
}
