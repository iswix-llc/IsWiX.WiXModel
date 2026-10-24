using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using IsWiX.WiXModel.Core;
using IsWiX.WiXModel.Core.Xml;
using Xunit;
using static IsWiX.WiXModel.Tests.TestText;

namespace IsWiX.WiXModel.Tests;

/// <summary>
/// A host that owns an <see cref="XDocument"/> (like IsWiX's DocumentManager) and edits it with plain LINQ to XML
/// still gets lossless, readable saves; and can view the same document through the typed model.
/// </summary>
public class HostIntegrationTests
{
    private static readonly XNamespace Wxs = WixNamespaces.Wxs;

    private const string Group = """
        <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
          <Fragment>
            <ComponentGroup Id="Product" Directory="INSTALLFOLDER">
              <Component Id="A">
                <File Source="a.exe" />
              </Component>
            </ComponentGroup>
          </Fragment>
        </Wix>
        """;

    private static XDocument LoadLossless(string text) => LosslessXml.Load(new MemoryStream(Encoding.UTF8.GetBytes(Lf(text))));

    [Fact]
    public void Appending_with_plain_linq_lines_up_the_new_element_and_the_end_tag()
    {
        var xml = LoadLossless(Group);
        var group = xml.Descendants(Wxs + "ComponentGroup").Single();

        group.Add(new XElement(Wxs + "Component", new XAttribute("Id", "B"), new XElement(Wxs + "File", new XAttribute("Source", "b.dll"))));

        Assert.Equal(
            Lf(Group).Replace(
                "      </Component>\n    </ComponentGroup>",
                "      </Component>\n      <Component Id=\"B\">\n        <File Source=\"b.dll\" />\n      </Component>\n    </ComponentGroup>",
                StringComparison.Ordinal),
            LosslessXml.ToXmlString(xml));
    }

    [Fact]
    public void Inserting_first_or_between_siblings_with_plain_linq_is_indented()
    {
        var xml = LoadLossless(Group);
        var group = xml.Descendants(Wxs + "ComponentGroup").Single();
        var existing = group.Elements().Single();

        group.AddFirst(new XElement(Wxs + "ComponentRef", new XAttribute("Id", "First")));
        existing.AddAfterSelf(new XElement(Wxs + "ComponentRef", new XAttribute("Id", "After")));

        Assert.Equal(
            Lf(Group)
                .Replace(
                    "<ComponentGroup Id=\"Product\" Directory=\"INSTALLFOLDER\">\n",
                    "<ComponentGroup Id=\"Product\" Directory=\"INSTALLFOLDER\">\n      <ComponentRef Id=\"First\" />\n",
                    StringComparison.Ordinal)
                .Replace(
                    "      </Component>\n",
                    "      </Component>\n      <ComponentRef Id=\"After\" />\n",
                    StringComparison.Ordinal),
            LosslessXml.ToXmlString(xml));
    }

    [Fact]
    public void Adding_to_a_self_closing_element_with_plain_linq_opens_it_on_new_lines()
    {
        var xml = LoadLossless("""
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
              <Fragment>
                <ComponentGroup Id="Empty" />
              </Fragment>
            </Wix>
            """);

        xml.Descendants(Wxs + "ComponentGroup").Single().Add(new XElement(Wxs + "ComponentRef", new XAttribute("Id", "C")));

        Assert.Equal(Lf("""
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
              <Fragment>
                <ComponentGroup Id="Empty">
                  <ComponentRef Id="C" />
                </ComponentGroup>
              </Fragment>
            </Wix>
            """), LosslessXml.ToXmlString(xml));
    }

    [Fact]
    public void Layout_follows_tabs_and_crlf_and_keeps_blank_lines()
    {
        var source = "<Wix xmlns=\"http://wixtoolset.org/schemas/v4/wxs\">\r\n\t<Fragment>\r\n\t\t<Property Id=\"A\" />\r\n\r\n\t</Fragment>\r\n</Wix>\r\n";
        var xml = LosslessXml.Load(new MemoryStream(Encoding.UTF8.GetBytes(source)));

        xml.Descendants(Wxs + "Fragment").Single().Add(new XElement(Wxs + "Property", new XAttribute("Id", "B")));

        Assert.Equal(
            "<Wix xmlns=\"http://wixtoolset.org/schemas/v4/wxs\">\r\n\t<Fragment>\r\n\t\t<Property Id=\"A\" />\r\n\r\n\t\t<Property Id=\"B\" />\r\n\t</Fragment>\r\n</Wix>\r\n",
            LosslessXml.ToXmlString(xml));
    }

    [Fact]
    public void Inline_content_and_text_are_not_laid_out()
    {
        var xml = LoadLossless("""
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
              <Fragment><Property Id="A" /></Fragment>
              <Fragment xml:space="preserve">
                <Property Id="B" />
              </Fragment>
            </Wix>
            """);
        var fragments = xml.Root!.Elements().ToList();

        fragments[0].Add(new XElement(Wxs + "Property", new XAttribute("Id", "A2")));
        fragments[1].Add(new XElement(Wxs + "Property", new XAttribute("Id", "B2")));

        Assert.Equal(Lf("""
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
              <Fragment><Property Id="A" /><Property Id="A2" /></Fragment>
              <Fragment xml:space="preserve">
                <Property Id="B" />
              <Property Id="B2" /></Fragment>
            </Wix>
            """), LosslessXml.ToXmlString(xml));
    }

    [Fact]
    public void Writing_lays_out_new_content_without_changing_the_tree()
    {
        var xml = LoadLossless(Group);
        var changes = 0;
        xml.Descendants(Wxs + "ComponentGroup").Single().Add(new XElement(Wxs + "ComponentRef", new XAttribute("Id", "X")));
        xml.Changed += (_, _) => changes++;

        var before = xml.ToString(SaveOptions.DisableFormatting);
        _ = LosslessXml.ToXmlString(xml);

        Assert.Equal(0, changes);
        Assert.Equal(before, xml.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void FromXml_wraps_a_host_owned_document_without_copying_it()
    {
        var xml = LoadLossless(Group);

        var document = Assert.IsType<WixDocument>(WixSourceDocument.FromXml(xml));
        Assert.Same(xml, document.Xml);
        Assert.True(document.IsLossless);
        Assert.Same(document.Root, WixDocument.FromXml(xml).Root);

        // A model edit is visible to the host, and a host edit to the model.
        document.Root.Fragments.Single().ComponentGroups.Single().Directory = "APPFOLDER";
        Assert.Equal("APPFOLDER", (string?)xml.Descendants(Wxs + "ComponentGroup").Single().Attribute("Directory"));
        xml.Descendants(Wxs + "Component").Single().SetAttributeValue("Guid", "*");
        Assert.Equal("*", document.Root.Fragments.Single().ComponentGroups.Single().Components.Single().Guid);
    }

    [Fact]
    public void FromXml_accepts_documents_not_loaded_losslessly_and_include_files()
    {
        var plain = WixSourceDocument.FromXml(XDocument.Parse(Lf(Group)));
        Assert.False(plain.IsLossless);
        Assert.Contains("<File Source=\"a.exe\" />", plain.ToString(), StringComparison.Ordinal);

        var include = WixIncludeDocument.FromXml(LoadLossless("<Include xmlns=\"http://wixtoolset.org/schemas/v4/wxs\"><?define X = 1 ?></Include>"));
        Assert.IsType<Include>(include.RootElement);
    }

    [Fact]
    public void FromXml_rejects_wix_v3_documents()
    {
        var v3 = XDocument.Parse("<Wix xmlns=\"http://schemas.microsoft.com/wix/2006/wi\"><Product /></Wix>");

        var error = Assert.Throws<WixDocumentException>(() => WixSourceDocument.FromXml(v3));
        Assert.Contains("wix convert", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Saving_to_a_path_replaces_the_file_and_leaves_no_temporary_file()
    {
        var folder = Path.Combine(Path.GetTempPath(), "IsWiX.WiXModel.Tests." + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "Product.wxs");
            System.IO.File.WriteAllText(path, Lf(Group));
            XDocument xml;
            using (var stream = System.IO.File.OpenRead(path))
            {
                xml = LosslessXml.Load(stream);
            }

            xml.Descendants(Wxs + "Component").Single().SetAttributeValue("Guid", "*");

            LosslessXml.Save(xml, path);

            Assert.Equal(Lf(Group).Replace("<Component Id=\"A\">", "<Component Id=\"A\" Guid=\"*\">", StringComparison.Ordinal), System.IO.File.ReadAllText(path));
            Assert.Equal(["Product.wxs"], System.IO.Directory.GetFiles(folder).Select(Path.GetFileName));
        }
        finally
        {
            System.IO.Directory.Delete(folder, recursive: true);
        }
    }
}
