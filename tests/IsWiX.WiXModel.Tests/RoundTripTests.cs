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
/// Runs over Corpus/*.wxs, plus every .wxs under the folders in WIXMODEL_CORPUS
/// (separated by ';'), e.g. a clone of the wixtoolset/wix test data.
/// </summary>
public class RoundTripTests
{
    public static TheoryData<string> CorpusFiles()
    {
        var data = new TheoryData<string>();
        var folders = new List<string> { Path.Combine(AppContext.BaseDirectory, "Corpus") };
        folders.AddRange((Environment.GetEnvironmentVariable("WIXMODEL_CORPUS") ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        foreach (var file in folders.SelectMany(f => IODirectory.EnumerateFiles(f, "*.wxs", SearchOption.AllDirectories)).Where(IsWixV4Source))
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
        var document = WixDocument.Load(new MemoryStream(source));

        Assert.True(document.IsLossless, "Source text capture fell back to normalized serialization.");
        TestText.AssertSameBytes(source, TestText.Save(document), Path.GetFileName(path));
    }

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void Reading_every_typed_property_changes_nothing(string path)
    {
        var source = IOFile.ReadAllBytes(path);
        var document = WixDocument.Load(new MemoryStream(source));

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

    [Fact]
    public void Utf16_document_round_trips()
    {
        var text = IOFile.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Corpus", "lf-spaces.wxs"));
        var encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
        var source = encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();

        var document = WixDocument.Load(new MemoryStream(source));

        Assert.Equal("utf-16", document.Format.Encoding.WebName);
        TestText.AssertSameBytes(source, TestText.Save(document), "UTF-16");
    }

    [Fact]
    public void Document_with_dtd_is_rejected()
    {
        var text = "<!DOCTYPE Wix [<!ENTITY x \"y\">]><Wix xmlns=\"http://wixtoolset.org/schemas/v4/wxs\">&x;</Wix>";

        Assert.Throws<WixDocumentException>(() => WixDocument.Parse(text));
    }

    private static bool IsWixV4Source(string path)
    {
        try
        {
            WixDocument.Load(path);
            return true;
        }
        catch (WixDocumentException)
        {
            // WiX v3 sources and non-WiX XML in an external corpus are out of scope.
            return false;
        }
    }
}
