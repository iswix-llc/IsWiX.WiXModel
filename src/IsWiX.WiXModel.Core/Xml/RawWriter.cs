using System;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace IsWiX.WiXModel.Core.Xml
{
    /// <summary>
    /// Serializes a document, reusing each node's captured source text while the node is unchanged and
    /// writing fresh, minimally escaped text for anything new or edited. Within a start tag, unchanged
    /// attributes keep their exact text and the whitespace before them, so multi-line attribute layouts survive edits.
    /// </summary>
    /// <remarks>
    /// Elements added with plain LINQ to XML (<c>XElement.Add</c> and friends, as older code does) carry no
    /// whitespace. The writer lays those out as it writes, without changing the tree: a new element goes on its
    /// own line, indented like its siblings, and a parent whose last child is new gets its end tag back on its
    /// own line. Parents whose existing children are inline, or that contain text, are written as they are.
    /// </remarks>
    internal sealed class RawWriter
    {
        private readonly StringBuilder _output = new StringBuilder();
        private readonly string _newLine;
        private readonly string _indent;

        private RawWriter(string newLine, string indent)
        {
            _newLine = newLine;
            _indent = indent;
        }

        public static string Write(XDocument document, XmlTextFormat format)
        {
            var writer = new RawWriter(format.NewLine, format.Indent);
            writer.WriteDocument(document);
            return writer._output.ToString();
        }

        private void WriteDocument(XDocument document)
        {
            var raw = document.Annotation<RawDocument>();
            if (raw != null)
            {
                _output.Append(raw.Declaration);
                foreach (var node in document.Nodes())
                {
                    _output.Append(node.Annotation<RawGap>()?.Text);
                    WriteNode(node);
                }

                _output.Append(raw.Trailer);
                return;
            }

            // A document built in code: one node per line.
            if (document.Declaration != null)
            {
                var declaration = document.Declaration;
                _output.Append("<?xml version=\"").Append(declaration.Version ?? "1.0").Append('"');
                if (!string.IsNullOrEmpty(declaration.Encoding))
                {
                    _output.Append(" encoding=\"").Append(declaration.Encoding).Append('"');
                }

                if (!string.IsNullOrEmpty(declaration.Standalone))
                {
                    _output.Append(" standalone=\"").Append(declaration.Standalone).Append('"');
                }

                _output.Append("?>").Append(_newLine);
            }

            foreach (var node in document.Nodes())
            {
                WriteNode(node);
                if (!(node is XText))
                {
                    _output.Append(_newLine);
                }
            }
        }

        private void WriteNode(XNode node)
        {
            switch (node)
            {
                case XElement element:
                    WriteElement(element, inheritedDefault: null, indent: string.Empty);
                    break;

                case XCData cdata:
                    if (!TryWriteRaw(cdata, cdata.Value))
                    {
                        _output.Append("<![CDATA[")
                            .Append(cdata.Value.Replace("]]>", "]]]]><![CDATA[>").Replace("\n", _newLine))
                            .Append("]]>");
                    }

                    break;

                case XText text:
                    if (!TryWriteRaw(text, text.Value))
                    {
                        AppendEscapedText(text.Value);
                    }

                    break;

                case XComment comment:
                    if (!TryWriteRaw(comment, comment.Value))
                    {
                        _output.Append("<!--").Append(comment.Value.Replace("\n", _newLine)).Append("-->");
                    }

                    break;

                case XProcessingInstruction pi:
                    if (!TryWriteRaw(pi, RawCapture.ProcessingInstructionValue(pi)))
                    {
                        _output.Append("<?").Append(pi.Target);
                        if (pi.Data.Length > 0)
                        {
                            _output.Append(' ').Append(pi.Data.Replace("\n", _newLine));
                        }

                        _output.Append("?>");
                    }

                    break;

                default:
                    throw new NotSupportedException($"{node.NodeType} nodes are not supported in WiX source files.");
            }
        }

        private bool TryWriteRaw(XNode node, string value)
        {
            var raw = node.Annotation<RawNode>();
            if (raw is null || !string.Equals(raw.ValueAtLoad, value, StringComparison.Ordinal))
            {
                return false;
            }

            _output.Append(raw.Text);
            return true;
        }

        /// <param name="element">The element.</param>
        /// <param name="inheritedDefault">A default namespace declared by a fresh ancestor, if any.</param>
        /// <param name="indent">The indentation of the line the element starts on.</param>
        private void WriteElement(XElement element, XNamespace? inheritedDefault, string indent)
        {
            var raw = element.Annotation<RawElement>();
            var keepName = raw != null && raw.Name == element.Name;
            string name;
            XNamespace? declareDefault = null;
            if (keepName)
            {
                name = raw!.RawName;
            }
            else
            {
                name = QualifiedElementName(element, inheritedDefault, out declareDefault);
            }

            _output.Append('<').Append(name);
            WriteAttributes(element, raw);
            if (declareDefault != null)
            {
                _output.Append(" xmlns=\"");
                AppendEscapedAttribute(declareDefault.NamespaceName, '"');
                _output.Append('"');
            }

            var endTag = keepName && raw!.EndTag != null ? raw.EndTag : "</" + name + ">";
            if (element.FirstNode is null)
            {
                if (raw != null && !raw.SelfClosing && !raw.HadNodes)
                {
                    // Written as <X></X> and still empty.
                    _output.Append(raw.Tail).Append(endTag);
                }
                else
                {
                    _output.Append(raw != null && raw.SelfClosing ? raw.Tail : " />");
                }

                return;
            }

            _output.Append(raw != null && !raw.SelfClosing ? raw.Tail : ">");
            WriteContent(element, declareDefault ?? inheritedDefault, indent);
            _output.Append(endTag);
        }

        private void WriteContent(XElement element, XNamespace? inheritedDefault, string indent)
        {
            var nodes = element.Nodes().ToList();
            if (!NeedsLayout(element, nodes))
            {
                foreach (var node in nodes)
                {
                    if (node is XElement child)
                    {
                        WriteElement(child, inheritedDefault, IndentBefore(child) ?? indent);
                    }
                    else
                    {
                        WriteNode(node);
                    }
                }

                return;
            }

            var childIndent = SiblingIndent(nodes) ?? indent + _indent;
            for (var i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (IsWhitespace(node) && i + 1 < nodes.Count && nodes[i + 1] is XElement next && IsNew(next))
                {
                    // Whitespace in front of a new element (often the parent's closing whitespace, when the element
                    // was appended): keep its line breaks, but line the element up with its siblings.
                    var lineBreaks = Math.Max(1, ((XText)node).Value.Count(c => c == '\n'));
                    for (var line = 0; line < lineBreaks; line++)
                    {
                        _output.Append(_newLine);
                    }

                    _output.Append(childIndent);
                    continue;
                }

                if (node is XElement child)
                {
                    if (IsNew(child))
                    {
                        if (i == 0 || !IsWhitespace(nodes[i - 1]))
                        {
                            _output.Append(_newLine).Append(childIndent);
                        }

                        WriteElement(child, inheritedDefault, childIndent);
                    }
                    else
                    {
                        WriteElement(child, inheritedDefault, IndentBefore(child) ?? childIndent);
                    }

                    continue;
                }

                WriteNode(node);
            }

            var last = nodes[nodes.Count - 1];
            if (!(IsWhitespace(last) && ((XText)last).Value.IndexOf('\n') >= 0))
            {
                _output.Append(_newLine).Append(indent);
            }
        }

        /// <summary>
        /// Gets whether the element has new children to lay out, and content that layout cannot disturb: its
        /// existing children are already one per line (or it has none), and it holds no text.
        /// </summary>
        private static bool NeedsLayout(XElement element, System.Collections.Generic.List<XNode> nodes)
        {
            if (!nodes.Any(n => n is XElement e && IsNew(e)) ||
                nodes.Any(n => n is XText && !IsWhitespace(n)) ||
                element.AncestorsAndSelf().Any(e => (string?)e.Attribute(XNamespace.Xml + "space") == "preserve"))
            {
                return false;
            }

            var hasExistingContent = nodes.Any(n => !IsWhitespace(n) && !(n is XElement e && IsNew(e)));
            var isLaidOut = nodes.Any(n => IsWhitespace(n) && ((XText)n).Value.IndexOf('\n') >= 0);
            return isLaidOut || !hasExistingContent;
        }

        /// <summary>Gets the indentation existing children use, preferring children that came from the source.</summary>
        private static string? SiblingIndent(System.Collections.Generic.List<XNode> nodes)
        {
            string? any = null;
            for (var i = 1; i < nodes.Count; i++)
            {
                if (nodes[i] is XElement element && IndentBefore(element) is { } indent)
                {
                    if (!IsNew(element))
                    {
                        return indent;
                    }

                    any ??= indent;
                }
            }

            return any;
        }

        private static string? IndentBefore(XNode node)
        {
            if (node.PreviousNode is XText text && IsWhitespace(text))
            {
                var newLine = text.Value.LastIndexOf('\n');
                if (newLine >= 0)
                {
                    return text.Value.Substring(newLine + 1);
                }
            }

            return null;
        }

        private static bool IsNew(XElement element) => element.Annotation<RawElement>() is null;

        private static bool IsWhitespace(XNode node) =>
            node is XText text && !(node is XCData) && text.Value.Trim(' ', '\t', '\r', '\n').Length == 0;

        private void WriteAttributes(XElement element, RawElement? raw)
        {
            var separator = " ";
            if (raw != null && raw.Attributes.Count > 0)
            {
                var last = raw.Attributes[raw.Attributes.Count - 1].LeadingWhitespace;
                if (last.IndexOf('\n') >= 0)
                {
                    // Attributes are laid out one per line; new ones follow suit.
                    separator = last;
                }
            }

            foreach (var attribute in element.Attributes())
            {
                var token = raw?.Attributes.FirstOrDefault(t => t.Name == attribute.Name);
                if (token != null && string.Equals(token.ValueAtLoad, attribute.Value, StringComparison.Ordinal))
                {
                    _output.Append(token.LeadingWhitespace).Append(token.Text);
                    continue;
                }

                var quote = token?.Quote ?? '"';
                _output.Append(token?.LeadingWhitespace ?? separator)
                    .Append(token?.RawName ?? QualifiedAttributeName(element, attribute))
                    .Append('=')
                    .Append(quote);
                AppendEscapedAttribute(attribute.Value, quote);
                _output.Append(quote);
            }
        }

        private static string QualifiedElementName(XElement element, XNamespace? inheritedDefault, out XNamespace? declareDefault)
        {
            declareDefault = null;
            var ns = element.Name.Namespace;
            if (ns == XNamespace.None || ns == (inheritedDefault ?? element.GetDefaultNamespace()))
            {
                return element.Name.LocalName;
            }

            var prefix = element.GetPrefixOfNamespace(ns);
            if (prefix != null)
            {
                return prefix + ":" + element.Name.LocalName;
            }

            // No declaration in scope (an element built in code and given a namespace nothing declares).
            declareDefault = ns;
            return element.Name.LocalName;
        }

        private static string QualifiedAttributeName(XElement element, XAttribute attribute)
        {
            var name = attribute.Name;
            if (attribute.IsNamespaceDeclaration)
            {
                return name.Namespace == XNamespace.None ? "xmlns" : "xmlns:" + name.LocalName;
            }

            if (name.Namespace == XNamespace.None)
            {
                return name.LocalName;
            }

            if (name.Namespace == XNamespace.Xml)
            {
                return "xml:" + name.LocalName;
            }

            var prefix = element.GetPrefixOfNamespace(name.Namespace)
                ?? throw new InvalidOperationException(
                    $"Attribute '{name.LocalName}' is in namespace '{name.NamespaceName}', which has no prefix declared. Add an xmlns:prefix attribute to the root element.");
            return prefix + ":" + name.LocalName;
        }

        private void AppendEscapedText(string value)
        {
            foreach (var c in value)
            {
                switch (c)
                {
                    case '&': _output.Append("&amp;"); break;
                    case '<': _output.Append("&lt;"); break;
                    case '>': _output.Append("&gt;"); break;
                    case '\r': _output.Append("&#xD;"); break;
                    case '\n': _output.Append(_newLine); break;
                    default: _output.Append(c); break;
                }
            }
        }

        private void AppendEscapedAttribute(string value, char quote)
        {
            foreach (var c in value)
            {
                switch (c)
                {
                    case '&': _output.Append("&amp;"); break;
                    case '<': _output.Append("&lt;"); break;
                    case '"' when quote == '"': _output.Append("&quot;"); break;
                    case '\'' when quote == '\'': _output.Append("&apos;"); break;
                    case '\t': _output.Append("&#x9;"); break;
                    case '\n': _output.Append("&#xA;"); break;
                    case '\r': _output.Append("&#xD;"); break;
                    default: _output.Append(c); break;
                }
            }
        }
    }
}
