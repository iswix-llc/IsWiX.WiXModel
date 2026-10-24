using System;
using System.Linq;
using IsWiX.WiXModel.Core;
using Xunit;
using static IsWiX.WiXModel.Tests.TestText;

namespace IsWiX.WiXModel.Tests;

/// <summary>The WiX Bal extension: bootstrapper application elements, and the attributes it adds to core elements.</summary>
public class BalTests
{
    private const string Source = """
        <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
          <Bundle Name="Acme" Manufacturer="Acme" Version="1.0.0.0" UpgradeCode="6f330b47-2577-43ad-9095-1861ba25889b">
            <BootstrapperApplication />
            <Variable Name="InstallFolder" Value="[ProgramFiles64Folder]Acme" />
            <Chain>
              <MsiPackage SourceFile="Acme.msi" />
            </Chain>
          </Bundle>
        </Wix>
        """;

    [Fact]
    public void Authoring_a_standard_bootstrapper_application()
    {
        var document = Parse(Source);
        var bundle = document.Root.Bundles.Single();

        bundle.BootstrapperApplications.Single().Add(new Bal.WixStandardBootstrapperApplication
        {
            Theme = Bal.WixStandardBootstrapperApplicationTheme.RtfLicense,
            LicenseFile = "License.rtf",
            SuppressOptionsUI = true,
        });
        bundle.Add(new Bal.Condition { Message = "Windows 10 or later is required.", ConditionAttribute = "VersionNT >= v10.0" });
        bundle.Variables.Single().BalOverridable = true;
        bundle.BalCommandLineVariables = Bal.CommandLineVariables.CaseInsensitive;

        Assert.Equal(Lf("""
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs" xmlns:bal="http://wixtoolset.org/schemas/v4/wxs/bal">
              <Bundle Name="Acme" Manufacturer="Acme" Version="1.0.0.0" UpgradeCode="6f330b47-2577-43ad-9095-1861ba25889b" bal:CommandLineVariables="caseInsensitive">
                <BootstrapperApplication>
                  <bal:WixStandardBootstrapperApplication Theme="rtfLicense" LicenseFile="License.rtf" SuppressOptionsUI="yes" />
                </BootstrapperApplication>
                <Variable Name="InstallFolder" Value="[ProgramFiles64Folder]Acme" bal:Overridable="yes" />
                <Chain>
                  <MsiPackage SourceFile="Acme.msi" />
                </Chain>
                <bal:Condition Message="Windows 10 or later is required." Condition="VersionNT >= v10.0" />
              </Bundle>
            </Wix>
            """), document.ToString());
    }

    [Fact]
    public void Bal_attributes_on_core_elements_read_typed_values()
    {
        var document = Parse("""
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs" xmlns:b="http://wixtoolset.org/schemas/v4/wxs/bal">
              <Fragment>
                <PackageGroup Id="Prereqs">
                  <ExePackage Id="Runtime" SourceFile="runtime.exe" b:PrereqPackage="yes" b:PrereqLicenseUrl="https://example.com/license" b:PrimaryPackageType="x64" />
                </PackageGroup>
              </Fragment>
            </Wix>
            """);
        var package = document.Root.Fragments.Single().PackageGroups.Single().ExePackages.Single();

        Assert.True(package.BalPrereqPackage!.Value.Value);
        Assert.Equal("https://example.com/license", package.BalPrereqLicenseUrl);
        Assert.Equal(Bal.PrimaryPackageType.X64, package.BalPrimaryPackageType!.Value.Value);

        package.BalPrereqPackage = null;
        package.BalPrereqLicenseFile = "License.rtf";

        // The file's own prefix is kept.
        Assert.Contains(
            "<ExePackage Id=\"Runtime\" SourceFile=\"runtime.exe\" b:PrereqLicenseUrl=\"https://example.com/license\" b:PrimaryPackageType=\"x64\" b:PrereqLicenseFile=\"License.rtf\" />",
            document.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_bal_attribute_values_on_core_elements_are_reported()
    {
        var document = Parse("""
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs" xmlns:b="http://wixtoolset.org/schemas/v4/wxs/bal">
              <Bundle Name="Acme" Manufacturer="Acme" Version="1.0.0.0" UpgradeCode="6f330b47-2577-43ad-9095-1861ba25889b" b:CommandLineVariables="$(var.Matching)">
                <Variable Name="InstallFolder" b:Overridable="maybe" />
                <Chain>
                  <MsiPackage SourceFile="Acme.msi" b:PrimaryPackageType="arm" b:PrereqPackage="yes" />
                </Chain>
              </Bundle>
            </Wix>
            """);

        var diagnostics = document.Validate();

        Assert.All(diagnostics, d => Assert.Equal(WixDiagnostics.InvalidValue, d.Code));
        Assert.Equal([Bal("Overridable"), Bal("PrimaryPackageType")], diagnostics.Select(d => d.Attribute));
        Assert.Contains("'maybe' is not a valid Variable/@b:Overridable; expected yes or no.", diagnostics[0].Message, StringComparison.Ordinal);
        Assert.Contains("expected default, x86, x64 or arm64", diagnostics[1].Message, StringComparison.Ordinal);
        Assert.Equal(5, diagnostics[1].Line);

        static System.Xml.Linq.XName Bal(string name) => WixNamespaces.Extension("bal") + name;
    }

    [Fact]
    public void Renamed_properties_name_their_attribute()
    {
        // DirectorySearchRef/@Parent clashes with WixElement.Parent; diagnostics would find it through this if it were typed.
        Assert.Equal(
            "Parent",
            typeof(DirectorySearchRef).GetProperty("ParentAttribute")!.GetCustomAttributes(typeof(WixAttributeAttribute), false)
                .Cast<WixAttributeAttribute>().Single().LocalName);
    }

    [Theory]
    [InlineData(typeof(Bal.WixStandardBootstrapperApplication), nameof(Bal.WixStandardBootstrapperApplication.ShowVersion), typeof(WixValue<bool>?))]
    [InlineData(typeof(Bal.Condition), nameof(Bal.Condition.ConditionAttribute), typeof(string))]
    [InlineData(typeof(Variable), nameof(Variable.BalOverridable), typeof(WixValue<bool>?))]
    [InlineData(typeof(MsiPackage), nameof(MsiPackage.BalPrimaryPackageType), typeof(WixValue<Bal.PrimaryPackageType>?))]
    [InlineData(typeof(Payload), nameof(Payload.BalBAFunctions), typeof(WixValue<bool>?))]
    public void Bal_types_map_to_csharp_types(Type element, string property, Type expected)
    {
        Assert.Equal(expected, element.GetProperty(property)!.PropertyType);
    }
}
