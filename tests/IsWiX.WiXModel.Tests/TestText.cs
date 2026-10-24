using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit.Sdk;

namespace IsWiX.WiXModel.Tests;

internal static class TestText
{
    /// <summary>Normalizes a raw string literal to LF, whatever line endings the test source was checked out with.</summary>
    public static string Lf(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    public static WixDocument Parse(string text) => WixDocument.Parse(Lf(text));

    public static byte[] Save(WixSourceDocument document)
    {
        using var stream = new MemoryStream();
        document.Save(stream);
        return stream.ToArray();
    }

    /// <summary>Asserts byte equality, reporting the first difference with escaped context.</summary>
    public static void AssertSameBytes(byte[] expected, byte[] actual, string because)
    {
        if (expected.AsSpan().SequenceEqual(actual))
        {
            return;
        }

        var index = Enumerable.Range(0, Math.Min(expected.Length, actual.Length)).FirstOrDefault(i => expected[i] != actual[i], Math.Min(expected.Length, actual.Length));
        throw new XunitException(
            $"{because}: output differs from the source at byte {index} (expected {expected.Length} bytes, got {actual.Length}).\n" +
            $"expected: {Context(expected, index)}\n" +
            $"actual:   {Context(actual, index)}");
    }

    private static string Context(byte[] bytes, int index)
    {
        var start = Math.Max(0, index - 40);
        var length = Math.Min(bytes.Length - start, 80);
        return Encoding.UTF8.GetString(bytes, start, Math.Max(0, length))
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);
    }
}
