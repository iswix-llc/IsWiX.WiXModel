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
/// Elements moved, re-sorted or removed with plain LINQ to XML (as IsWiX's automation classes do) are laid out
/// cleanly on save, while everything still where it was loaded keeps its exact text.
/// </summary>
public class MovedElementLayoutTests
{
    private static readonly XNamespace Wxs = WixNamespaces.Wxs;

    private const string Source = """
        <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
          <Package Name="Sample" Manufacturer="Acme" Version="1.0.0.0">
            <MajorUpgrade DowngradeErrorMessage="Newer version installed." />
            <Property Id="BETA" Value="2" />
            <Property Id="DELTA" Value="4" />
            <Feature Id="Main" />
          </Package>
        </Wix>
        """;

    private static XDocument Load(string text) => LosslessXml.Load(new MemoryStream(Encoding.UTF8.GetBytes(Lf(text))));

    private static XElement Package(XDocument xml) => xml.Root!.Element(Wxs + "Package")!;

    [Fact]
    public void Remove_and_re_add_to_sort_writes_one_element_per_line()
    {
        // What IsWiXProperties.Create + SortXML do: add after the canonical predecessor (Feature), then remove every
        // Property and add them back after it in Id order.
        var xml = Load(Source);
        var package = Package(xml);
        var feature = package.Element(Wxs + "Feature")!;
        feature.AddAfterSelf(new XElement(Wxs + "Property", new XAttribute("Id", "ALPHA"), new XAttribute("Value", "1")));
        var sorted = package.Elements(Wxs + "Property").OrderBy(p => (string)p.Attribute("Id")!).ToArray();
        package.Elements(Wxs + "Property").Remove();
        foreach (var property in Enumerable.Reverse(sorted))
        {
            feature.AddAfterSelf(property);
        }

        Assert.Equal(Lf("""
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
              <Package Name="Sample" Manufacturer="Acme" Version="1.0.0.0">
                <MajorUpgrade DowngradeErrorMessage="Newer version installed." />
                <Feature Id="Main" />
                <Property Id="ALPHA" Value="1" />
                <Property Id="BETA" Value="2" />
                <Property Id="DELTA" Value="4" />
              </Package>
            </Wix>
            """), LosslessXml.ToXmlString(xml));
    }

    [Fact]
    public void Removing_with_plain_linq_leaves_no_blank_line()
    {
        var xml = Load(Source);

        Package(xml).Elements(Wxs + "Property").First().Remove();

        Assert.Equal(Lf(Source).Replace("    <Property Id=\"BETA\" Value=\"2\" />\n", string.Empty, StringComparison.Ordinal), LosslessXml.ToXmlString(xml));
    }

    [Fact]
    public void Removing_the_only_child_leaves_the_parent_tidy()
    {
        var xml = Load("""
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
              <Fragment>
                <Property Id="ONLY" />
              </Fragment>
            </Wix>
            """);

        xml.Root!.Element(Wxs + "Fragment")!.Elements().Single().Remove();

        Assert.Equal(Lf("""
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
              <Fragment>
              </Fragment>
            </Wix>
            """), LosslessXml.ToXmlString(xml));
    }

    [Fact]
    public void Inserting_before_an_existing_element_keeps_both_on_their_own_lines()
    {
        var xml = Load(Source);
        var delta = Package(xml).Elements(Wxs + "Property").Last();

        delta.AddBeforeSelf(new XElement(Wxs + "Property", new XAttribute("Id", "GAMMA"), new XAttribute("Value", "3")));

        Assert.Equal(
            Lf(Source).Replace(
                "    <Property Id=\"DELTA\"",
                "    <Property Id=\"GAMMA\" Value=\"3\" />\n    <Property Id=\"DELTA\"",
                StringComparison.Ordinal),
            LosslessXml.ToXmlString(xml));
    }

    [Fact]
    public void Moving_an_element_to_the_end_lays_it_out_there()
    {
        var xml = Load(Source);
        var package = Package(xml);
        var majorUpgrade = package.Element(Wxs + "MajorUpgrade")!;

        majorUpgrade.Remove();
        package.Add(majorUpgrade);

        Assert.Equal(Lf("""
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
              <Package Name="Sample" Manufacturer="Acme" Version="1.0.0.0">
                <Property Id="BETA" Value="2" />
                <Property Id="DELTA" Value="4" />
                <Feature Id="Main" />
                <MajorUpgrade DowngradeErrorMessage="Newer version installed." />
              </Package>
            </Wix>
            """), LosslessXml.ToXmlString(xml));
    }

    [Fact]
    public void Elements_still_where_they_were_keep_their_own_layout()
    {
        // An oddly indented, multi-line element next to an edit is left exactly as written.
        var source = """
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
              <Package Name="Sample" Manufacturer="Acme" Version="1.0.0.0">
                    <Property Id="ODD"
                              Value="x" />
                <Property Id="ZULU" Value="26" />
              </Package>
            </Wix>
            """;
        var xml = Load(source);
        var package = Package(xml);
        var zulu = package.Elements().Last();

        zulu.Remove();
        package.Add(new XElement(Wxs + "Property", new XAttribute("Id", "YANKEE")), zulu);

        Assert.Equal(
            Lf(source).Replace(
                "    <Property Id=\"ZULU\" Value=\"26\" />\n",
                "    <Property Id=\"YANKEE\" />\n    <Property Id=\"ZULU\" Value=\"26\" />\n",
                StringComparison.Ordinal),
            LosslessXml.ToXmlString(xml));
    }

    [Fact]
    public void A_file_consistently_indented_its_own_way_keeps_that_style()
    {
        // Children at 3 spaces under a 2-space parent (the file's unit is 2): two siblings agree, so moved
        // elements follow them rather than the standard parent + unit.
        var source = """
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
              <Fragment>
                 <Property Id="B" />
                 <Property Id="C" />
                 <Property Id="A" />
              </Fragment>
            </Wix>
            """;
        var xml = Load(source);
        var fragment = xml.Root!.Element(Wxs + "Fragment")!;
        var a = fragment.Elements().Last();

        a.Remove();
        fragment.AddFirst(a);

        Assert.Equal(Lf("""
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
              <Fragment>
                 <Property Id="A" />
                 <Property Id="B" />
                 <Property Id="C" />
              </Fragment>
            </Wix>
            """), LosslessXml.ToXmlString(xml));
    }

    [Fact]
    public void Moved_elements_follow_crlf_and_tabs()
    {
        var source = "<Wix xmlns=\"http://wixtoolset.org/schemas/v4/wxs\">\r\n\t<Fragment>\r\n\t\t<Property Id=\"B\" />\r\n\t\t<Property Id=\"A\" />\r\n\t</Fragment>\r\n</Wix>\r\n";
        var xml = LosslessXml.Load(new MemoryStream(Encoding.UTF8.GetBytes(source)));
        var fragment = xml.Root!.Element(Wxs + "Fragment")!;
        var sorted = fragment.Elements().OrderBy(p => (string)p.Attribute("Id")!).ToArray();

        fragment.Elements().Remove();
        fragment.Add(sorted);

        Assert.Equal(
            "<Wix xmlns=\"http://wixtoolset.org/schemas/v4/wxs\">\r\n\t<Fragment>\r\n\t\t<Property Id=\"A\" />\r\n\t\t<Property Id=\"B\" />\r\n\t</Fragment>\r\n</Wix>\r\n",
            LosslessXml.ToXmlString(xml));
    }

    [Fact]
    public void Unchanged_document_is_still_byte_for_byte()
    {
        var xml = Load(Source);

        Assert.True(LosslessXml.IsLossless(xml));
        Assert.Equal(Lf(Source), LosslessXml.ToXmlString(xml));
    }
}
