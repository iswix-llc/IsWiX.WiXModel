using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IsWiX.WiXModel.SchemaGen.CodeGen;

internal static class Naming
{
    private static readonly string[] UncountableEndings = ["Media", "Information", "Data"];

    /// <summary>Turns a schema value such as <c>perMachine</c>, <c>always64</c> or <c>.net</c> into a C# identifier.</summary>
    public static string Pascal(string value)
    {
        var result = new StringBuilder();
        var upperNext = true;
        foreach (var c in value)
        {
            if (!char.IsLetterOrDigit(c))
            {
                upperNext = true;
                continue;
            }

            result.Append(upperNext ? char.ToUpperInvariant(c) : c);
            upperNext = false;
        }

        if (result.Length == 0 || char.IsDigit(result[0]))
        {
            result.Insert(0, '_');
        }

        return result.ToString();
    }

    /// <summary>
    /// Gets the collection property name for a child element: <c>Directory</c> becomes <c>Directories</c>.
    /// Names that are already plural or uncountable (<c>Files</c>, <c>Media</c>, <c>SummaryInformation</c>,
    /// <c>ConfigurationData</c>) become <c>FilesElements</c> and so on, so they never take the name a singular
    /// sibling (<c>File</c> becomes <c>Files</c>) needs.
    /// </summary>
    public static string Plural(string name)
    {
        if ((name.EndsWith('s') && !name.EndsWith("ss", StringComparison.Ordinal) && !name.EndsWith("us", StringComparison.Ordinal)) ||
            UncountableEndings.Any(ending => name.EndsWith(ending, StringComparison.Ordinal)))
        {
            return name + "Elements";
        }

        if (name.EndsWith('y') && name.Length > 1 && !"aeiou".Contains(char.ToLowerInvariant(name[^2])))
        {
            return name[..^1] + "ies";
        }

        if (name.EndsWith('s') || name.EndsWith('x') || name.EndsWith("ch", StringComparison.Ordinal) || name.EndsWith("sh", StringComparison.Ordinal))
        {
            return name + "es";
        }

        return name + "s";
    }

    public static string Unique(string name, HashSet<string> used, string suffix = "")
    {
        var candidate = used.Contains(name) ? name + suffix : name;
        var counter = 2;
        while (used.Contains(candidate) || candidate.Length == 0)
        {
            candidate = name + suffix + counter++;
        }

        used.Add(candidate);
        return candidate;
    }

    /// <summary>Collapses schema documentation into paragraphs of single-spaced text.</summary>
    public static IReadOnlyList<string> Paragraphs(string? documentation)
    {
        if (string.IsNullOrWhiteSpace(documentation))
        {
            return [];
        }

        var lines = documentation.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim());
        var paragraphs = new List<string>();
        var current = new StringBuilder();
        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                Flush();
                continue;
            }

            if (current.Length > 0)
            {
                current.Append(' ');
            }

            current.Append(line);
        }

        Flush();
        return paragraphs;

        void Flush()
        {
            if (current.Length > 0)
            {
                paragraphs.Add(current.ToString());
                current.Clear();
            }
        }
    }

    public static string EscapeXml(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    public static string Literal(string text) =>
        "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
