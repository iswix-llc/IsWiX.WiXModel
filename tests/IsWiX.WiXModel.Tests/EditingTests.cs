using System;
using System.Linq;
using System.Xml.Linq;
using IsWiX.WiXModel.Core;
using IsWiX.WiXModel.Core.Xml;
using Xunit;
using static IsWiX.WiXModel.Tests.TestText;

namespace IsWiX.WiXModel.Tests;

/// <summary>Edits touch only what they change and follow the file's own layout.</summary>
public class EditingTests
{
    private const string MultiLinePackage = """
        <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
          <Package Name='Sample'
                   Manufacturer="Acme &amp; Co"
                   Version="1.0.0.0">
          </Package>
        </Wix>
        """;

    private const string Tree = """
        <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
          <Package Name="S" Manufacturer="M" Version="1.0">
            <Directory Id="INSTALLFOLDER" Name="Sample">
              <Component Id="A">
                <File Source="a.exe" />
              </Component>
            </Directory>
          </Package>
        </Wix>
        """;

    [Fact]
    public void Changing_an_attribute_rewrites_only_that_attribute()
    {
        var document = Parse(MultiLinePackage);

        document.Root.Packages.Single().Version = "2.0.0.0";

        Assert.Equal(Lf(MultiLinePackage).Replace("1.0.0.0", "2.0.0.0", StringComparison.Ordinal), document.ToString());
    }

    [Fact]
    public void New_attribute_follows_a_one_per_line_layout()
    {
        var document = Parse(MultiLinePackage);

        document.Root.Packages.Single().Scope = PackageScopeType.PerUser;

        Assert.Equal(
            Lf(MultiLinePackage).Replace(
                "Version=\"1.0.0.0\">",
                "Version=\"1.0.0.0\"\n           Scope=\"perUser\">",
                StringComparison.Ordinal),
            document.ToString());
    }

    [Fact]
    public void Removing_an_attribute_removes_its_whitespace()
    {
        var document = Parse(MultiLinePackage);

        document.Root.Packages.Single().Manufacturer = null;

        Assert.Equal(Lf(MultiLinePackage).Replace("\n           Manufacturer=\"Acme &amp; Co\"", string.Empty, StringComparison.Ordinal), document.ToString());
    }

    [Fact]
    public void Added_subtree_is_indented_like_its_siblings()
    {
        var document = Parse(Tree);
        var directory = document.Root.Packages.Single().Directories.Single();

        var component = new Component { Id = "B" };
        component.Add(new File { Source = "b.dll" });
        directory.Add(component);

        Assert.Equal(
            Lf(Tree).Replace(
                "      </Component>\n",
                "      </Component>\n      <Component Id=\"B\">\n        <File Source=\"b.dll\" />\n      </Component>\n",
                StringComparison.Ordinal),
            document.ToString());
    }

    [Fact]
    public void Adding_to_a_self_closing_element_opens_it()
    {
        var document = Parse("""
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
              <Package Name="S" Manufacturer="M" Version="1.0">
                <Directory Id="X" />
              </Package>
            </Wix>
            """);

        document.Root.Packages.Single().Directories.Single().Add(new Component { Id = "C" });

        Assert.Equal(Lf("""
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
              <Package Name="S" Manufacturer="M" Version="1.0">
                <Directory Id="X">
                  <Component Id="C" />
                </Directory>
              </Package>
            </Wix>
            """), document.ToString());
    }

    [Fact]
    public void Removing_an_element_removes_its_line_and_collapses_an_emptied_parent()
    {
        var document = Parse(Tree);
        var directory = document.Root.Packages.Single().Directories.Single();

        directory.Components.Single().RemoveFromParent();

        Assert.Equal(Lf("""
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
              <Package Name="S" Manufacturer="M" Version="1.0">
                <Directory Id="INSTALLFOLDER" Name="Sample" />
              </Package>
            </Wix>
            """), document.ToString());
    }

    [Fact]
    public void New_document_is_formatted_with_the_default_format()
    {
        var document = WixDocument.Create();

        var package = document.Root.Add(new Package { Name = "Sample", Manufacturer = "Acme", Version = "1.0.0.0", Scope = PackageScopeType.PerMachine });
        var directory = package.Add(new Directory { Id = "INSTALLFOLDER", Name = "Sample" });
        directory.Add(new Component { Bitness = BitnessType.Always64 }).Add(new File { Source = "App.exe", KeyPath = true });

        Assert.Equal(
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
            "<Wix xmlns=\"http://wixtoolset.org/schemas/v4/wxs\">\r\n" +
            "    <Package Name=\"Sample\" Manufacturer=\"Acme\" Version=\"1.0.0.0\" Scope=\"perMachine\">\r\n" +
            "        <Directory Id=\"INSTALLFOLDER\" Name=\"Sample\">\r\n" +
            "            <Component Bitness=\"always64\">\r\n" +
            "                <File Source=\"App.exe\" KeyPath=\"yes\" />\r\n" +
            "            </Component>\r\n" +
            "        </Directory>\r\n" +
            "    </Package>\r\n" +
            "</Wix>\r\n",
            document.ToString());
        Assert.False(document.Format.HasByteOrderMark);
    }

    [Fact]
    public void Editing_a_crlf_file_writes_crlf()
    {
        var document = WixDocument.Parse(Lf(Tree).Replace("\n", "\r\n", StringComparison.Ordinal));

        document.Root.Packages.Single().Directories.Single().Add(new Component { Id = "B" });

        Assert.Contains("</Component>\r\n      <Component Id=\"B\" />\r\n    </Directory>", document.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Typed_values_distinguish_literals_expressions_and_invalid_text()
    {
        var document = Parse("""
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
              <Package Name="S" Manufacturer="M" Version="1.0">
                <Component KeyPath="$(var.KeyPath)" DiskId="2" Bitness="always64" Location="sideways">
                  <File Source="a.txt" Compressed="true" />
                </Component>
              </Package>
            </Wix>
            """);
        var component = document.Root.Packages.Single().Components.Single();

        Assert.Equal(WixValueKind.Expression, component.KeyPath!.Value.Kind);
        Assert.Equal("$(var.KeyPath)", component.KeyPath.Value.Text);
        Assert.Equal(2, component.DiskId!.Value.Value);
        Assert.Equal(BitnessType.Always64, component.Bitness!.Value.Value);
        Assert.Equal(WixValueKind.Invalid, component.Location!.Value.Kind);
        Assert.Throws<InvalidOperationException>(() => component.Location.Value.Value);
        Assert.Equal(YesNoDefaultType.Yes, component.Files.Single().Compressed!.Value.Value);
        Assert.Null(component.Permanent);

        component.KeyPath = true;
        component.DiskId = WixValue<int>.FromExpression("$(var.Disk)");

        Assert.Contains("<Component KeyPath=\"yes\" DiskId=\"$(var.Disk)\"", document.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Enum_converters_list_and_check_attribute_text_for_editors()
    {
        var converter = WixEnumConverters.PackageScopeType;

        Assert.Equal(
            ["perMachine", "perMachineOrUser", "perUser", "perUserOrMachine"],
            Enum.GetValues<PackageScopeType>().Select(converter.Format).Order(StringComparer.Ordinal));
        Assert.Equal(PackageScopeType.PerUser, converter.Parse("perUser").Value);
        Assert.Equal(WixValueKind.Expression, converter.Parse("$(var.Scope)").Kind);
        Assert.Equal(WixValueKind.Invalid, converter.Parse("everyone").Kind);
    }

    [Fact]
    public void Expression_factory_rejects_plain_text()
    {
        Assert.Throws<ArgumentException>(() => WixValue<bool>.FromExpression("maybe"));
    }

    [Fact]
    public void Wrappers_are_stable_and_unknown_elements_are_kept()
    {
        var document = WixDocument.Load(System.IO.Path.Combine(AppContext.BaseDirectory, "Corpus", "lf-spaces.wxs"));
        var package = document.Root.Packages.Single();

        Assert.Same(package, document.Root.Packages.Single());
        Assert.Same(package, package.Children.First().Parent);

        Assert.DoesNotContain(package.Children, e => e is WixUnknownElement);

        // Extension elements with no registered wrapper are still exposed, and kept.
        var crlf = WixDocument.Load(System.IO.Path.Combine(AppContext.BaseDirectory, "Corpus", "crlf-bom-tabs.wxs"));
        var component = crlf.Root.Packages.Single().Directories.Single().Directories.Single().Components.First();
        var unknown = component.Children.OfType<WixUnknownElement>().Select(e => e.Xml.Name.LocalName).ToList();
        Assert.Equal(["RemoveFolderEx"], unknown);
    }

    [Fact]
    public void Extension_attributes_are_reachable_by_name()
    {
        XNamespace custom = "urn:example:custom";
        var document = WixDocument.Load(System.IO.Path.Combine(AppContext.BaseDirectory, "Corpus", "extension-attributes.wxs"));
        var file = document.Root.Packages.Single().Directories.Single().Components.Single().Files.Single();

        Assert.Equal("sha256", file.GetAttributeValue(custom + "Hash"));
        Assert.Equal(FileProcessorArchitecture.X64, file.ProcessorArchitecture!.Value.Value);
        Assert.Equal(FileAssembly.No, file.Assembly!.Value.Value);

        file.SetAttributeValue(custom + "Hash", "sha512");

        Assert.Contains("custom:Hash=\"sha512\"", document.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Adding_an_element_that_already_has_a_parent_throws()
    {
        var document = Parse(Tree);
        var package = document.Root.Packages.Single();
        var component = package.Directories.Single().Components.Single();

        Assert.Throws<InvalidOperationException>(() => package.Add(component));

        component.RemoveFromParent();
        package.Add(component);
        Assert.Same(package, component.Parent);
    }

    [Fact]
    public void Wix_v3_source_is_rejected_with_a_conversion_hint()
    {
        var error = Assert.Throws<WixDocumentException>(() =>
            WixDocument.Parse("<Wix xmlns=\"http://schemas.microsoft.com/wix/2006/wi\"><Product /></Wix>"));

        Assert.Contains("wix convert", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Detected_format_matches_the_source()
    {
        var document = WixDocument.Load(System.IO.Path.Combine(AppContext.BaseDirectory, "Corpus", "crlf-bom-tabs.wxs"));

        Assert.Equal("\r\n", document.Format.NewLine);
        Assert.Equal("\t", document.Format.Indent);
        Assert.True(document.Format.HasByteOrderMark);
        Assert.True(LosslessXml.IsLossless(document.Xml));
    }
}
