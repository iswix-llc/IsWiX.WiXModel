using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace IsWiX.WiXModel.Core.Xml
{
    /// <summary>
    /// Walks a parsed document alongside its source text and annotates every node with the text it came
    /// from. XmlReader has already validated the XML, so this only has to find node boundaries; any
    /// disagreement between the two throws <see cref="RawCaptureException"/> and the caller falls back to
    /// ordinary serialization.
    /// </summary>
    internal sealed class RawCapture
    {
        private readonly string _text;
        private int _pos;

        private RawCapture(string text) => _text = text;

        public static void Capture(XDocument document, string text) => new RawCapture(text).CaptureDocument(document);

        public static void Clear(XDocument document)
        {
            document.RemoveAnnotations<RawDocument>();
            foreach (var node in document.DescendantNodes())
            {
                node.RemoveAnnotations<RawNode>();
                node.RemoveAnnotations<RawGap>();
                node.RemoveAnnotations<RawElement>();
            }
        }

        private void CaptureDocument(XDocument document)
        {
            string? declaration = null;
            if (At("<?xml") && _pos + 5 < _text.Length && (IsWhitespace(_text[_pos + 5]) || _text[_pos + 5] == '?'))
            {
                declaration = ReadThrough("?>");
            }

            foreach (var node in document.Nodes())
            {
                if (!(node is XText))
                {
                    var gap = ReadWhitespace();
                    if (gap.Length > 0)
                    {
                        node.AddAnnotation(new RawGap(gap));
                    }
                }

                CaptureNode(node);
            }

            var trailer = ReadWhitespace();
            if (_pos != _text.Length)
            {
                throw Fail("content after the root element");
            }

            document.AddAnnotation(new RawDocument(declaration, trailer));
        }

        private void CaptureNode(XNode node)
        {
            switch (node)
            {
                case XCData cdata:
                    Expect("<![CDATA[");
                    var cdataText = "<![CDATA[" + ReadThrough("]]>");
                    cdata.AddAnnotation(new RawNode(cdataText, cdata.Value));
                    break;

                case XText text:
                    var start = _pos;
                    var end = _text.IndexOf('<', _pos);
                    _pos = end < 0 ? _text.Length : end;
                    var raw = _text.Substring(start, _pos - start);
                    if (!string.Equals(Decode(NormalizeNewLines(raw)), text.Value, StringComparison.Ordinal))
                    {
                        throw Fail("text node");
                    }

                    text.AddAnnotation(new RawNode(raw, text.Value));
                    break;

                case XComment comment:
                    Expect("<!--");
                    var commentText = "<!--" + ReadThrough("-->");
                    if (!string.Equals(NormalizeNewLines(commentText.Substring(4, commentText.Length - 7)), comment.Value, StringComparison.Ordinal))
                    {
                        throw Fail("comment");
                    }

                    comment.AddAnnotation(new RawNode(commentText, comment.Value));
                    break;

                case XProcessingInstruction pi:
                    Expect("<?");
                    var piText = "<?" + ReadThrough("?>");
                    if (!piText.Substring(2).StartsWith(pi.Target, StringComparison.Ordinal))
                    {
                        throw Fail("processing instruction");
                    }

                    pi.AddAnnotation(new RawNode(piText, ProcessingInstructionValue(pi)));
                    break;

                case XElement element:
                    CaptureElement(element);
                    break;

                default:
                    throw Fail(node.NodeType.ToString());
            }
        }

        private void CaptureElement(XElement element)
        {
            Expect("<");
            var nameStart = _pos;
            while (_pos < _text.Length && !IsWhitespace(_text[_pos]) && _text[_pos] != '/' && _text[_pos] != '>')
            {
                _pos++;
            }

            var rawName = _text.Substring(nameStart, _pos - nameStart);
            if (!string.Equals(LocalPart(rawName), element.Name.LocalName, StringComparison.Ordinal))
            {
                throw Fail($"element <{rawName}>");
            }

            var attributes = element.Attributes().ToList();
            var tokens = new List<RawAttribute>(attributes.Count);
            string tail;
            bool selfClosing;
            while (true)
            {
                var whitespace = ReadWhitespace();
                if (At("/>"))
                {
                    _pos += 2;
                    tail = whitespace + "/>";
                    selfClosing = true;
                    break;
                }

                if (At(">"))
                {
                    _pos++;
                    tail = whitespace + ">";
                    selfClosing = false;
                    break;
                }

                var attributeStart = _pos;
                while (_pos < _text.Length && _text[_pos] != '=' && !IsWhitespace(_text[_pos]))
                {
                    _pos++;
                }

                var attributeName = _text.Substring(attributeStart, _pos - attributeStart);
                ReadWhitespace();
                Expect("=");
                ReadWhitespace();
                var quote = _pos < _text.Length ? _text[_pos] : '\0';
                if (quote != '"' && quote != '\'')
                {
                    throw Fail($"attribute {attributeName}");
                }

                var close = _text.IndexOf(quote, _pos + 1);
                if (close < 0)
                {
                    throw Fail($"attribute {attributeName}");
                }

                _pos = close + 1;
                if (tokens.Count >= attributes.Count)
                {
                    throw Fail($"attribute {attributeName}");
                }

                var attribute = attributes[tokens.Count];
                if (!string.Equals(LocalPart(attributeName), attribute.Name.LocalName, StringComparison.Ordinal))
                {
                    throw Fail($"attribute {attributeName}");
                }

                tokens.Add(new RawAttribute(
                    attribute.Name,
                    whitespace,
                    _text.Substring(attributeStart, _pos - attributeStart),
                    attributeName,
                    quote,
                    attribute.Value));
            }

            if (tokens.Count != attributes.Count)
            {
                throw Fail($"attributes of <{rawName}>");
            }

            var raw = new RawElement(element.Name, rawName, tokens, tail, selfClosing, element.FirstNode != null, element.PreviousNode);
            element.AddAnnotation(raw);
            if (selfClosing)
            {
                return;
            }

            foreach (var child in element.Nodes())
            {
                CaptureNode(child);
            }

            Expect("</");
            raw.EndTag = "</" + ReadThrough(">");
        }

        internal static string ProcessingInstructionValue(XProcessingInstruction pi) => pi.Target + "\0" + pi.Data;

        internal static string NormalizeNewLines(string text) =>
            text.IndexOf('\r') < 0 ? text : text.Replace("\r\n", "\n").Replace('\r', '\n');

        /// <summary>Expands the five predefined entities and character references.</summary>
        private static string Decode(string text)
        {
            if (text.IndexOf('&') < 0)
            {
                return text;
            }

            var result = new StringBuilder(text.Length);
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] != '&')
                {
                    result.Append(text[i]);
                    continue;
                }

                var semicolon = text.IndexOf(';', i);
                var entity = text.Substring(i + 1, semicolon - i - 1);
                switch (entity)
                {
                    case "lt": result.Append('<'); break;
                    case "gt": result.Append('>'); break;
                    case "amp": result.Append('&'); break;
                    case "quot": result.Append('"'); break;
                    case "apos": result.Append('\''); break;
                    default:
                        var code = entity.StartsWith("#x", StringComparison.Ordinal)
                            ? int.Parse(entity.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                            : int.Parse(entity.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture);
                        result.Append(char.ConvertFromUtf32(code));
                        break;
                }

                i = semicolon;
            }

            return result.ToString();
        }

        private static string LocalPart(string qualifiedName)
        {
            var colon = qualifiedName.IndexOf(':');
            return colon < 0 ? qualifiedName : qualifiedName.Substring(colon + 1);
        }

        private static bool IsWhitespace(char c) => c == ' ' || c == '\t' || c == '\r' || c == '\n';

        private bool At(string token) => string.CompareOrdinal(_text, _pos, token, 0, token.Length) == 0;

        private void Expect(string token)
        {
            if (!At(token))
            {
                throw Fail($"'{token}'");
            }

            _pos += token.Length;
        }

        /// <summary>Reads up to and including <paramref name="terminator"/>, returning the text after the current position.</summary>
        private string ReadThrough(string terminator)
        {
            var index = _text.IndexOf(terminator, _pos, StringComparison.Ordinal);
            if (index < 0)
            {
                throw Fail($"'{terminator}'");
            }

            var end = index + terminator.Length;
            var result = _text.Substring(_pos, end - _pos);
            _pos = end;
            return result;
        }

        private string ReadWhitespace()
        {
            var start = _pos;
            while (_pos < _text.Length && IsWhitespace(_text[_pos]))
            {
                _pos++;
            }

            return _text.Substring(start, _pos - start);
        }

        private RawCaptureException Fail(string what) =>
            new RawCaptureException($"Source text did not match the parsed {what} at offset {_pos}.");
    }

#pragma warning disable CA1032, CA1064 // Internal signal caught by LosslessXml; never escapes.
    internal sealed class RawCaptureException : Exception
    {
        public RawCaptureException(string message)
            : base(message)
        {
        }
    }
#pragma warning restore CA1032, CA1064
}
