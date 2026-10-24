using System;
using System.Linq;
using System.Xml.Linq;
using IsWiX.WiXModel.Core;
using Xunit;
using static IsWiX.WiXModel.Tests.TestText;

namespace IsWiX.WiXModel.Tests;

/// <summary>Extension namespaces get a prefix declared on the root when the model adds something that uses them.</summary>
public class NamespaceDeclarationTests
{
    private static readonly XNamespace Util = WixNamespaces.Extension("util");

    private const string Source = """
        <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
          <Fragment>
            <Property Id="ALPHA" />
          </Fragment>
        </Wix>
        """;

    [Fact]
    public void Adding_an_extension_element_declares_its_usual_prefix()
    {
        var document = Parse(Source);
        var fragment = document.Root.Fragments.Single();

        var added = fragment.AddElement(new XElement(Util + "QueryWindowsWellKnownSIDs"));

        Assert.IsType<IsWiX.WiXModel.Util.QueryWindowsWellKnownSIDs>(added);
        Assert.Equal(Lf("""
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs" xmlns:util="http://wixtoolset.org/schemas/v4/wxs/util">
              <Fragment>
                <Property Id="ALPHA" />
                <util:QueryWindowsWellKnownSIDs />
              </Fragment>
            </Wix>
            """), document.ToString());
    }

    [Fact]
    public void A_namespace_already_declared_under_another_prefix_keeps_it()
    {
        var source = Source.Replace("v4/wxs\">", "v4/wxs\" xmlns:u=\"http://wixtoolset.org/schemas/v4/wxs/util\">", StringComparison.Ordinal);
        var document = Parse(source);

        document.Root.Fragments.Single().AddElement(new XElement(Util + "QueryWindowsWellKnownSIDs"));

        Assert.Equal(
            Lf(source).Replace("    <Property Id=\"ALPHA\" />\n", "    <Property Id=\"ALPHA\" />\n    <u:QueryWindowsWellKnownSIDs />\n", StringComparison.Ordinal),
            document.ToString());
    }

    [Fact]
    public void Setting_an_extension_attribute_declares_its_namespace()
    {
        var document = Parse(Source);
        var property = document.Root.Fragments.Single().Properties.Single();

        property.SetAttributeValue(XNamespace.Get("urn:acme:custom") + "Note", "hello");

        Assert.Equal(Lf("""
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs" xmlns:custom="urn:acme:custom">
              <Fragment>
                <Property Id="ALPHA" custom:Note="hello" />
              </Fragment>
            </Wix>
            """), document.ToString());
    }

    [Fact]
    public void Extension_namespaces_inside_an_added_subtree_are_declared_too()
    {
        var document = Parse(Source);
        var component = new Component { Id = "Users", Directory = "INSTALLFOLDER" };
        component.Xml.Add(new XElement(Util + "User", new XAttribute("Id", "Svc"), new XAttribute(Util + "Note", "x")));
        component.Xml.SetAttributeValue(WixNamespaces.Extension("netfx") + "Native", "yes");

        document.Root.Fragments.Single().Add(component);

        var text = document.ToString();
        Assert.StartsWith(
            "<Wix xmlns=\"http://wixtoolset.org/schemas/v4/wxs\" xmlns:netfx=\"http://wixtoolset.org/schemas/v4/wxs/netfx\" xmlns:util=\"http://wixtoolset.org/schemas/v4/wxs/util\">",
            text, StringComparison.Ordinal);
        Assert.Contains("<Component Id=\"Users\" Directory=\"INSTALLFOLDER\" netfx:Native=\"yes\">", text, StringComparison.Ordinal);
        Assert.Contains("<util:User Id=\"Svc\" util:Note=\"x\" />", text, StringComparison.Ordinal);
        Assert.DoesNotContain("p1:", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_prefix_taken_by_another_namespace_gets_a_number()
    {
        var source = Source.Replace("v4/wxs\">", "v4/wxs\" xmlns:custom=\"urn:other:custom\">", StringComparison.Ordinal);
        var document = Parse(source);

        document.Root.Fragments.Single().Properties.Single().SetAttributeValue(XNamespace.Get("urn:acme:custom") + "Note", "hello");

        Assert.Contains("xmlns:custom2=\"urn:acme:custom\"", document.ToString(), StringComparison.Ordinal);
        Assert.Contains("custom2:Note=\"hello\"", document.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Preferred_prefixes_come_from_wix_or_the_namespace()
    {
        Assert.Equal("fw", WixNamespaces.PreferredPrefix(WixNamespaces.Extension("firewall")));
        Assert.Equal("dep", WixNamespaces.PreferredPrefix(WixNamespaces.Extension("dependency")));
        Assert.Equal("custom", WixNamespaces.PreferredPrefix("urn:acme:custom"));
        Assert.Equal("tools", WixNamespaces.PreferredPrefix("http://acme.example/schemas/tools"));
        Assert.Equal("ext", WixNamespaces.PreferredPrefix("urn:1:2"));
    }

    [Fact]
    public void Core_elements_and_unchanged_documents_are_untouched()
    {
        var document = Parse(Source);

        document.Root.Fragments.Single().Add(new Property { Id = "BRAVO" });

        Assert.DoesNotContain("xmlns:", document.ToString(), StringComparison.Ordinal);
    }
}
