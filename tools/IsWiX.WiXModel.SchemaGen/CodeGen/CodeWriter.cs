using System.Collections.Generic;
using System.Text;

namespace IsWiX.WiXModel.SchemaGen.CodeGen;

/// <summary>Writes indented C# with LF line endings, so output is identical on every platform.</summary>
internal sealed class CodeWriter
{
    private const int DocumentationWidth = 110;
    private readonly StringBuilder _text = new();
    private int _depth;

    public CodeWriter Line(string line = "")
    {
        if (line.Length > 0)
        {
            _text.Append(' ', _depth * 4).Append(line);
        }

        _text.Append('\n');
        return this;
    }

    public CodeWriter Open(string line)
    {
        Line(line);
        Line("{");
        _depth++;
        return this;
    }

    public CodeWriter Close(string suffix = "")
    {
        _depth--;
        return Line("}" + suffix);
    }

    /// <summary>Writes a <c>&lt;summary&gt;</c> (and optional <c>&lt;remarks&gt;</c>) doc comment, word-wrapped.</summary>
    public CodeWriter Documentation(IReadOnlyList<string> summary, IReadOnlyList<string>? remarks = null)
    {
        Block("summary", summary.Count == 0 ? ["Not documented in the WiX schema."] : summary);
        if (remarks is { Count: > 0 })
        {
            Block("remarks", remarks);
        }

        return this;
    }

    public override string ToString() => _text.ToString();

    private void Block(string tag, IReadOnlyList<string> paragraphs)
    {
        Line($"/// <{tag}>");
        for (var i = 0; i < paragraphs.Count; i++)
        {
            var wrapped = paragraphs.Count > 1;
            if (wrapped)
            {
                Line("/// <para>");
            }

            foreach (var line in Wrap(Naming.EscapeXml(paragraphs[i])))
            {
                Line("/// " + line);
            }

            if (wrapped)
            {
                Line("/// </para>");
            }
        }

        Line($"/// </{tag}>");
    }

    private static IEnumerable<string> Wrap(string text)
    {
        var line = new StringBuilder();
        foreach (var word in text.Split(' '))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > DocumentationWidth)
            {
                yield return line.ToString();
                line.Clear();
            }

            if (line.Length > 0)
            {
                line.Append(' ');
            }

            line.Append(word);
        }

        if (line.Length > 0)
        {
            yield return line.ToString();
        }
    }
}
