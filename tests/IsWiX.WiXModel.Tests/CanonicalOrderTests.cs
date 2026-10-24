using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using IsWiX.WiXModel.Core;
using IsWiX.WiXModel.SchemaGen.Schema;
using Xunit;
using static IsWiX.WiXModel.Tests.TestText;

namespace IsWiX.WiXModel.Tests;

/// <summary>Canonical order: positions depend only on content, so edits on different branches merge cleanly.</summary>
public class CanonicalOrderTests
{
    private const string Source = """
        <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
          <Package Name="Sample" Manufacturer="Acme" Version="1.0.0.0">
            <MajorUpgrade DowngradeErrorMessage="Newer version installed." />
            <StandardDirectory Id="ProgramFiles6432Folder" />
            <Feature Id="Main" />
            <Property Id="BRAVO" Value="2" />
            <Property Id="DELTA" Value="4" />
            <UIRef Id="WixUI_Minimal" />
          </Package>
        </Wix>
        """;

    private static WixDocument Canonical(string text)
    {
        var document = Parse(text);
        document.Ordering = CanonicalOrder.Create();
        return document;
    }

    [Fact]
    public void Rules_only_name_children_the_schema_allows()
    {
        var schema = SchemaLoader.Load(Path.Combine(GeneratorTests.RepositoryRoot(), "schemas", "wix"));
        var ordering = CanonicalOrder.Create();

        foreach (var parent in new[] { "Package", "Fragment" })
        {
            var allowed = schema.Elements[parent].ChildElements;
            var listed = ordering.ChildKinds(WixNamespaces.Wxs + parent).Select(n => n.LocalName).ToList();

            Assert.NotEmpty(listed);
            Assert.Empty(listed.Except(allowed));
        }
    }

    [Fact]
    public void Properties_are_added_in_id_order_at_their_canonical_place()
    {
        var document = Canonical(Source);
        var package = document.Root.Packages.Single();

        package.Add(new Property { Id = "CHARLIE", Value = "3" });
        package.Add(new Property { Id = "ALPHA", Value = "1" });
        package.Add(new Property { Id = "ECHO", Value = "5" });

        Assert.Equal(
            Lf(Source).Replace(
                "    <Property Id=\"BRAVO\" Value=\"2\" />\n    <Property Id=\"DELTA\" Value=\"4\" />\n",
                "    <Property Id=\"ALPHA\" Value=\"1\" />\n    <Property Id=\"BRAVO\" Value=\"2\" />\n    <Property Id=\"CHARLIE\" Value=\"3\" />\n" +
                "    <Property Id=\"DELTA\" Value=\"4\" />\n    <Property Id=\"ECHO\" Value=\"5\" />\n",
                StringComparison.Ordinal),
            document.ToString());
    }

    [Fact]
    public void Kind_order_places_features_after_major_upgrade_and_directories()
    {
        // IsWiX's old list spelled it "Majorupgrade", so features sorted above MajorUpgrade; fixed here.
        var document = Canonical(Source);

        document.Root.Packages.Single().Add(new Feature { Id = "Docs" });

        Assert.Equal(
            Lf(Source).Replace("    <Feature Id=\"Main\" />", "    <Feature Id=\"Docs\" />\n    <Feature Id=\"Main\" />", StringComparison.Ordinal),
            document.ToString());
    }

    [Fact]
    public void The_same_content_gives_the_same_file_whatever_order_it_was_added_in()
    {
        var one = Canonical(Source);
        var two = Canonical(Source);
        var packageOne = one.Root.Packages.Single();
        var packageTwo = two.Root.Packages.Single();

        packageOne.Add(new Property { Id = "ZULU" });
        packageOne.Add(new Feature { Id = "Extras" });
        packageOne.Add(new MediaTemplate { EmbedCab = true });
        packageOne.Add(new Property { Id = "ALPHA" });

        packageTwo.Add(new Property { Id = "ALPHA" });
        packageTwo.Add(new MediaTemplate { EmbedCab = true });
        packageTwo.Add(new Property { Id = "ZULU" });
        packageTwo.Add(new Feature { Id = "Extras" });

        Assert.Equal(one.ToString(), two.ToString());
    }

    [Fact]
    public void Canonically_first_element_goes_in_front()
    {
        var document = Canonical(Source);

        document.Root.Packages.Single().Add(new SummaryInformation { Description = "Sample" });

        Assert.Equal(
            Lf(Source).Replace(
                "    <MajorUpgrade",
                "    <SummaryInformation Description=\"Sample\" />\n    <MajorUpgrade",
                StringComparison.Ordinal),
            document.ToString());
    }

    [Fact]
    public void Unlisted_and_extension_elements_go_last_and_keep_their_order()
    {
        var document = Canonical("""
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs" xmlns:util="http://wixtoolset.org/schemas/v4/wxs/util">
              <Fragment>
                <util:Group Id="Admins" />
                <Property Id="B" />
              </Fragment>
            </Wix>
            """);

        Assert.Equal(1, document.Canonicalize());
        document.Root.Fragments.Single().Add(new Property { Id = "A" });

        Assert.Equal(Lf("""
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs" xmlns:util="http://wixtoolset.org/schemas/v4/wxs/util">
              <Fragment>
                <Property Id="A" />
                <Property Id="B" />
                <util:Group Id="Admins" />
              </Fragment>
            </Wix>
            """), document.ToString());
    }

    [Fact]
    public void Canonicalize_sorts_a_scrambled_package_and_comments_travel_with_their_element()
    {
        var document = Canonical("""
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
              <Package Name="Sample" Manufacturer="Acme" Version="1.0.0.0">
                <Property Id="DELTA" Value="4" />
                <!-- Upgrades replace older versions. -->
                <MajorUpgrade DowngradeErrorMessage="Newer version installed." />
                <Property Id="BRAVO" Value="2" />
                <Feature Id="Main" />
              </Package>
            </Wix>
            """);

        Assert.Equal(1, document.Canonicalize());

        Assert.Equal(Lf("""
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
              <Package Name="Sample" Manufacturer="Acme" Version="1.0.0.0">
                <!-- Upgrades replace older versions. -->
                <MajorUpgrade DowngradeErrorMessage="Newer version installed." />
                <Feature Id="Main" />
                <Property Id="BRAVO" Value="2" />
                <Property Id="DELTA" Value="4" />
              </Package>
            </Wix>
            """), document.ToString());
    }

    [Fact]
    public void Canonicalizing_a_canonical_document_changes_nothing()
    {
        var document = Canonical(Source);

        Assert.Equal(0, document.Canonicalize());
        Assert.True(document.IsLossless);
        Assert.Equal(Lf(Source), document.ToString());
    }

    [Fact]
    public void Parents_with_preprocessor_conditionals_are_not_reordered()
    {
        var source = """
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
              <Fragment>
                <Property Id="ZULU" />
                <?if $(sys.BUILDARCH) = x64 ?>
                <Property Id="ALPHA" Value="64" />
                <?else?>
                <Property Id="ALPHA" Value="32" />
                <?endif?>
              </Fragment>
            </Wix>
            """;
        var document = Canonical(source);
        var fragment = document.Root.Fragments.Single();

        Assert.Equal(0, document.Canonicalize());
        fragment.Add(new Property { Id = "BRAVO" });

        Assert.Equal(
            Lf(source).Replace("    <?endif?>\n", "    <?endif?>\n    <Property Id=\"BRAVO\" />\n", StringComparison.Ordinal),
            document.ToString());
    }

    [Fact]
    public void Components_sort_by_file_source_and_files_by_source()
    {
        var document = Canonical("""
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
              <Fragment>
                <ComponentGroup Id="Product" Directory="INSTALLFOLDER">
                  <Component Id="owfB" Directory="INSTALLFOLDER">
                    <File Source="$(var.SourceDir)\b.dll" />
                  </Component>
                  <Component Id="owfA" Directory="INSTALLFOLDER">
                    <File Source="$(var.SourceDir)\a.exe" KeyPath="yes" />
                  </Component>
                </ComponentGroup>
              </Fragment>
            </Wix>
            """);
        var group = document.Root.Fragments.Single().ComponentGroups.Single();

        group.Add(new Component { Id = "owfC", Directory = "INSTALLFOLDER" }).Add(new File { Source = "$(var.SourceDir)\\c.txt" });
        document.Canonicalize();

        Assert.Equal(["owfA", "owfB", "owfC"], group.Components.Select(c => c.Id));
        Assert.Equal(Lf("""
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
              <Fragment>
                <ComponentGroup Id="Product" Directory="INSTALLFOLDER">
                  <Component Id="owfA" Directory="INSTALLFOLDER">
                    <File Source="$(var.SourceDir)\a.exe" KeyPath="yes" />
                  </Component>
                  <Component Id="owfB" Directory="INSTALLFOLDER">
                    <File Source="$(var.SourceDir)\b.dll" />
                  </Component>
                  <Component Id="owfC" Directory="INSTALLFOLDER">
                    <File Source="$(var.SourceDir)\c.txt" />
                  </Component>
                </ComponentGroup>
              </Fragment>
            </Wix>
            """), document.ToString());
    }

    [Fact]
    public void Without_an_ordering_add_appends_after_the_last_sibling_of_the_same_kind()
    {
        var document = Parse(Source);

        document.Root.Packages.Single().Add(new Property { Id = "ALPHA", Value = "1" });

        Assert.Null(document.Ordering);
        Assert.Throws<InvalidOperationException>(() => document.Canonicalize());
        Assert.Contains("<Property Id=\"DELTA\" Value=\"4\" />\n    <Property Id=\"ALPHA\" Value=\"1\" />", document.ToString(), StringComparison.Ordinal);
    }
}
