using System;
using System.Linq;
using Xunit;
using static IsWiX.WiXModel.Tests.TestText;

namespace IsWiX.WiXModel.Tests;

/// <summary>Duplicate Ids, invalid attribute values and unresolved references, found without building.</summary>
public class DiagnosticsTests
{
    [Fact]
    public void A_duplicate_id_is_reported_on_the_second_definition()
    {
        var document = Parse("""
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
              <Fragment>
                <Property Id="ALPHA" Value="1" />
                <Property Id="BRAVO" Value="2" />
                <Property Id="ALPHA" Value="3" />
              </Fragment>
            </Wix>
            """);

        var diagnostic = Assert.Single(document.Validate());

        Assert.Equal(WixDiagnostics.DuplicateId, diagnostic.Code);
        Assert.Equal(WixDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal((5, 15), (diagnostic.Line!.Value, diagnostic.Column!.Value));
        Assert.Equal("Id", diagnostic.Attribute);
        Assert.Contains("the first is line 3", diagnostic.Message, StringComparison.Ordinal);
        Assert.StartsWith("(5,15): error WXM001:", diagnostic.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Ids_in_conditional_blocks_and_scoped_ids_are_not_duplicates()
    {
        var document = Parse("""
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
              <Fragment>
                <?if $(sys.BUILDARCH) = x64 ?>
                <Property Id="ARCH" Value="64" />
                <?else?>
                <Property Id="ARCH" Value="32" />
                <?endif?>
                <Property Id="$(var.Name)" />
                <Property Id="$(var.Name)" />
                <StandardDirectory Id="ProgramFiles6432Folder" />
                <CustomTable Id="One">
                  <Column Id="Id" PrimaryKey="yes" Type="string" />
                </CustomTable>
                <CustomTable Id="Two">
                  <Column Id="Id" PrimaryKey="yes" Type="string" />
                </CustomTable>
              </Fragment>
            </Wix>
            """);

        Assert.Empty(document.Validate());
    }

    [Fact]
    public void Standard_directories_repeat_and_access_modifiers_are_understood()
    {
        var document = Parse("""
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
              <Fragment>
                <StandardDirectory Id="ProgramFiles6432Folder">
                  <Directory Id="virtual INSTALLFOLDER" Name="Acme" />
                </StandardDirectory>
                <StandardDirectory Id="ProgramFiles6432Folder">
                  <Directory Id="override INSTALLFOLDER" Name="Acme Tools" />
                  <Directory Id="private Logs" Name="Logs" />
                </StandardDirectory>
                <Property Id="public SHARED" />
                <Property Id="SHARED" />
              </Fragment>
            </Wix>
            """);

        // Only the property: "public SHARED" and "SHARED" are the same symbol.
        var diagnostic = Assert.Single(document.Validate());
        Assert.Equal(WixDiagnostics.DuplicateId, diagnostic.Code);
        Assert.Equal("Property", diagnostic.Element.Name.LocalName);
    }

    [Fact]
    public void Invalid_attribute_values_list_what_is_allowed()
    {
        var document = Parse("""
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
              <Fragment>
                <Component Id="One" Directory="INSTALLFOLDER" KeyPath="maybe" DiskId="two" Location="$(var.Where)">
                  <File Source="a.txt" Compressed="true" />
                </Component>
                <ServiceInstall Id="Svc" Name="Svc" Start="sometimes" ErrorControl="normal" Type="ownProcess" />
              </Fragment>
            </Wix>
            """);

        var diagnostics = document.Validate();

        Assert.All(diagnostics, d => Assert.Equal(WixDiagnostics.InvalidValue, d.Code));
        Assert.Equal(["KeyPath", "DiskId", "Start"], diagnostics.Select(d => d.Attribute!.LocalName));
        Assert.Contains("'maybe' is not a valid Component/@KeyPath; expected yes or no.", diagnostics[0].Message, StringComparison.Ordinal);
        Assert.Contains("expected a number", diagnostics[1].Message, StringComparison.Ordinal);
        Assert.Contains("expected auto, demand, disabled", diagnostics[2].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void References_are_checked_across_the_files_of_a_build()
    {
        var product = Parse("""
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
              <Package Name="Sample" Manufacturer="Acme" Version="1.0.0.0">
                <StandardDirectory Id="ProgramFiles6432Folder">
                  <Merge Id="Runtime" SourceFile="runtime.msm" DiskId="1" Language="0" />
                </StandardDirectory>
                <Property Id="SHARED" />
                <Feature Id="Main">
                  <ComponentGroupRef Id="Files" />
                  <ComponentGroupRef Id="Missing" />
                  <MergeRef Id="Runtime" />
                  <MergeRef Id="Other" />
                </Feature>
              </Package>
            </Wix>
            """);
        var files = Parse("""
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
              <Fragment>
                <Property Id="SHARED" />
                <ComponentGroup Id="Files" />
              </Fragment>
            </Wix>
            """);

        var diagnostics = WixDiagnostics.Validate(product, files);

        Assert.Equal(
            ["WXM003 ComponentGroupRef Missing", "WXM003 MergeRef Other", "WXM001 Property SHARED"],
            diagnostics.Select(d => $"{d.Code} {d.Element.Name.LocalName} {d.Element.Attribute("Id")!.Value}"));
        Assert.Same(files, diagnostics[2].Document);
        Assert.Contains("of another file", diagnostics[2].Message, StringComparison.Ordinal);
        Assert.All(diagnostics.Take(2), d => Assert.Equal(WixDiagnosticSeverity.Warning, d.Severity));

        // On its own, a file's references to other files are expected.
        Assert.Empty(product.Validate());
    }

    [Fact]
    public void Content_added_in_code_has_no_line()
    {
        var document = Parse("""
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
              <Fragment>
                <Property Id="ALPHA" />
              </Fragment>
            </Wix>
            """);

        document.Root.Fragments.Single().Add(new Property { Id = "ALPHA" });

        var diagnostic = Assert.Single(document.Validate());
        Assert.Null(diagnostic.Line);
        Assert.StartsWith("error WXM001:", diagnostic.ToString(), StringComparison.Ordinal);
    }
}
