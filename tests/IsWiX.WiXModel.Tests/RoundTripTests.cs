using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using IsWiX.WiXModel.Core;
using Xunit;
using IODirectory = System.IO.Directory;
using IOFile = System.IO.File;

namespace IsWiX.WiXModel.Tests;

/// <summary>
/// The core promise: loading and saving a file without edits reproduces it byte-for-byte.
/// Runs over Corpus/*.wxs and *.wxi, plus every .wxs and .wxi under the folders in WIXMODEL_CORPUS
/// (separated by ';'), e.g. a clone of the wixtoolset/wix test data.
/// </summary>
public class RoundTripTests
{
    public static TheoryData<string> CorpusFiles()
    {
        var data = new TheoryData<string>();
        var folders = new List<string> { Path.Combine(AppContext.BaseDirectory, "Corpus") };
        folders.AddRange((System.Environment.GetEnvironmentVariable("WIXMODEL_CORPUS") ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        foreach (var file in folders.SelectMany(f => IODirectory.EnumerateFiles(f, "*.*", SearchOption.AllDirectories).Where(IsWixSourceName)).Where(IsWixV4Source))
        {
            data.Add(file);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void Unchanged_document_saves_byte_for_byte(string path)
    {
        var source = IOFile.ReadAllBytes(path);
        var document = WixSourceDocument.Load(new MemoryStream(source));

        Assert.True(document.IsLossless, "Source text capture fell back to normalized serialization.");
        TestText.AssertSameBytes(source, TestText.Save(document), Path.GetFileName(path));
    }

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void Reading_every_typed_property_changes_nothing(string path)
    {
        var source = IOFile.ReadAllBytes(path);
        var document = WixSourceDocument.Load(new MemoryStream(source));

        foreach (var element in document.Xml.Root!.DescendantsAndSelf().Select(WixElement.Wrap))
        {
            foreach (var property in element.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetValue(element) is IEnumerable sequence and not string)
                {
                    foreach (var _ in sequence)
                    {
                    }
                }
            }
        }

        TestText.AssertSameBytes(source, TestText.Save(document), Path.GetFileName(path));
    }

    /// <summary>
    /// Guards the corpus run against passing vacuously: if the loader started rejecting real files, they would
    /// drop out of <see cref="CorpusFiles"/> instead of failing. WiX's own tests include a few v3 and malformed
    /// files on purpose, so the bar is 95% (v7.0.0: 913 of 924).
    /// </summary>
    [Fact]
    public void External_corpus_files_are_almost_all_loadable()
    {
        var folders = (System.Environment.GetEnvironmentVariable("WIXMODEL_CORPUS") ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (folders.Length == 0)
        {
            return;
        }

        var candidates = folders.SelectMany(f => IODirectory.EnumerateFiles(f, "*.*", SearchOption.AllDirectories).Where(IsWixSourceName)).ToList();
        var loadable = candidates.Count(IsWixV4Source);

        Assert.NotEmpty(candidates);
        Assert.True(
            loadable >= candidates.Count * 0.95,
            $"Only {loadable} of {candidates.Count} corpus files loaded as WiX v4+ sources.");
    }

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void Canonicalizing_keeps_every_node_and_is_idempotent(string path)
    {
        var document = WixSourceDocument.Load(new MemoryStream(IOFile.ReadAllBytes(path)));
        document.Ordering = CanonicalOrder.Create();
        var before = Inventory(document.Xml);

        document.Canonicalize();
        var text = document.ToString();

        Assert.Equal(before, Inventory(document.Xml));
        Assert.Equal(0, document.Canonicalize());
        var reloaded = WixSourceDocument.Parse(text);
        reloaded.Ordering = CanonicalOrder.Create();
        Assert.Equal(0, reloaded.Canonicalize());
        Assert.Equal(text, reloaded.ToString());
    }

    /// <summary>Every element (with its attributes and parent), comment and processing instruction, in a fixed order.</summary>
    private static List<string> Inventory(System.Xml.Linq.XDocument xml) =>
        xml.DescendantNodes()
            .Select(n => n switch
            {
                System.Xml.Linq.XElement e => $"E {e.Parent?.Name} > {e.Name} " + string.Join(" ", e.Attributes().Select(a => $"{a.Name}={a.Value}")),
                System.Xml.Linq.XComment c => $"C {c.Parent?.Name} {c.Value}",
                System.Xml.Linq.XProcessingInstruction pi => $"P {pi.Parent?.Name} {pi.Target} {pi.Data}",
                _ => null,
            })
            .Where(s => s != null)
            .Select(s => s!)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void Utf16_document_round_trips()
    {
        var text = IOFile.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Corpus", "lf-spaces.wxs"));
        var encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
        var source = encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();

        var document = WixSourceDocument.Load(new MemoryStream(source));

        Assert.Equal("utf-16", document.Format.Encoding.WebName);
        TestText.AssertSameBytes(source, TestText.Save(document), "UTF-16");
    }

    [Fact]
    public void Document_with_dtd_is_rejected()
    {
        var text = "<!DOCTYPE Wix [<!ENTITY x \"y\">]><Wix xmlns=\"http://wixtoolset.org/schemas/v4/wxs\">&x;</Wix>";

        Assert.Throws<WixDocumentException>(() => WixDocument.Parse(text));
    }

    private static bool IsWixSourceName(string path) =>
        path.EndsWith(".wxs", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".wxi", StringComparison.OrdinalIgnoreCase);

    private static bool IsWixV4Source(string path)
    {
        try
        {
            WixSourceDocument.Load(path);
            return true;
        }
        catch (WixDocumentException)
        {
            // WiX v3 sources and non-WiX XML in an external corpus are out of scope.
            return false;
        }
    }
}
