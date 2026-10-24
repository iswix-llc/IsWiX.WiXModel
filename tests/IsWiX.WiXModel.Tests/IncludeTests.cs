using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using IsWiX.WiXModel.Core;
using Xunit;

namespace IsWiX.WiXModel.Tests;

/// <summary>Include (.wxi) files and opening either kind of source file.</summary>
public class IncludeTests
{
    private static string CorpusPath(string name) => Path.Combine(AppContext.BaseDirectory, "Corpus", name);

    [Fact]
    public void Include_contents_are_typed_and_defines_are_kept()
    {
        var include = WixIncludeDocument.Load(CorpusPath("common.wxi"));

        Assert.Equal(["ARPNOMODIFY", "ARPHELPLINK"], include.Root.Children.OfType<Property>().Select(p => p.Id));
        Assert.Equal("!(loc.NewerVersionInstalled)", include.Root.Children.OfType<MajorUpgrade>().Single().DowngradeErrorMessage);
        Assert.DoesNotContain(include.Root.Children, e => e is WixUnknownElement);

        var instructions = include.Root.Xml.Nodes().OfType<XProcessingInstruction>().Select(pi => pi.Target).ToList();
        Assert.Equal(["define", "define", "if", "define", "else", "define", "endif"], instructions);
    }

    [Fact]
    public void Adding_to_an_include_matches_its_layout()
    {
        var path = CorpusPath("common.wxi");
        var include = WixIncludeDocument.Load(path);

        include.Root.Add(new Property { Id = "ARPNOREPAIR", Value = "1" });

        var expected = System.IO.File.ReadAllText(path).Replace(
            "  <Property Id=\"ARPHELPLINK\" Value=\"https://acme.example/help\" />\n",
            "  <Property Id=\"ARPHELPLINK\" Value=\"https://acme.example/help\" />\n  <Property Id=\"ARPNOREPAIR\" Value=\"1\" />\n",
            StringComparison.Ordinal);
        Assert.Equal(expected, include.ToString());
    }

    [Fact]
    public void New_include_is_formatted_with_the_default_format()
    {
        var include = WixIncludeDocument.Create();

        include.Root.Add(new Property { Id = "ARPNOMODIFY", Value = "1" });

        Assert.Equal(
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
            "<Include xmlns=\"http://wixtoolset.org/schemas/v4/wxs\">\r\n" +
            "    <Property Id=\"ARPNOMODIFY\" Value=\"1\" />\r\n" +
            "</Include>\r\n",
            include.ToString());
    }

    [Fact]
    public void Source_document_load_returns_the_right_kind_of_document()
    {
        var include = Assert.IsType<WixIncludeDocument>(WixSourceDocument.Load(CorpusPath("common.wxi")));
        Assert.IsType<Include>(include.RootElement);

        var source = Assert.IsType<WixDocument>(WixSourceDocument.Load(CorpusPath("lf-spaces.wxs")));
        Assert.IsType<Wix>(source.RootElement);
    }

    [Fact]
    public void Loading_with_the_wrong_class_says_which_class_to_use()
    {
        var asSource = Assert.Throws<WixDocumentException>(() => WixDocument.Load(CorpusPath("common.wxi")));
        Assert.Contains("WixIncludeDocument", asSource.Message, StringComparison.Ordinal);

        var asInclude = Assert.Throws<WixDocumentException>(() => WixIncludeDocument.Load(CorpusPath("lf-spaces.wxs")));
        Assert.Contains("WixDocument", asInclude.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Wix_v3_include_is_rejected_with_a_conversion_hint()
    {
        var error = Assert.Throws<WixDocumentException>(() =>
            WixSourceDocument.Parse("<Include xmlns=\"http://schemas.microsoft.com/wix/2006/wi\"><?define X = 1 ?></Include>"));

        Assert.Contains("wix convert", error.Message, StringComparison.Ordinal);
    }
}
