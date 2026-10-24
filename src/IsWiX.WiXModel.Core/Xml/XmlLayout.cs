using System.Linq;
using System.Xml.Linq;

namespace IsWiX.WiXModel.Core.Xml
{
    /// <summary>
    /// Keeps hand-edited files readable when the model adds or removes elements: new elements are indented
    /// like their siblings and removed elements take their line with them. With a <see cref="WixOrdering"/>
    /// attached to the document, a new element goes at its canonical position; otherwise after the last sibling
    /// of the same kind, or at the end.
    /// </summary>
    internal static class XmlLayout
    {
        public static void InsertElement(XElement parent, XElement child)
        {
            InsertElementCore(parent, child);
            if (parent.Document != null)
            {
                // Extension elements and attributes get xmlns:prefix on the root, not a made-up p1 prefix.
                WixNamespaces.DeclareAll(child);
            }
        }

        private static void InsertElementCore(XElement parent, XElement child)
        {
            if (parent.Document is null)
            {
                // Detached trees are laid out when they are added to a document.
                parent.Add(child);
                return;
            }

            var indent = LosslessXml.GetFormat(parent.Document).Indent;
            XNode? anchor;
            var ordering = WixOrdering.Get(parent.Document);
            if (ordering != null && ordering.TryFindPredecessor(parent, child, out var predecessor))
            {
                anchor = predecessor;
                if (anchor is null && parent.Elements().FirstOrDefault() is { } first)
                {
                    // Canonically first: go in front of the current first element, on its line.
                    var firstIndent = IndentOf(first) ?? IndentOf(parent) + indent;
                    first.AddBeforeSelf(child, new XText("\n" + firstIndent));
                    FormatSubtree(child, firstIndent, indent);
                    return;
                }
            }
            else
            {
                anchor = (XNode?)parent.Elements(child.Name).LastOrDefault()
                    ?? parent.Nodes().LastOrDefault(n => !IsWhitespace(n));
            }

            string childIndent;
            if (anchor != null)
            {
                childIndent = IndentOf(anchor) ?? IndentOf(parent) + indent;
                anchor.AddAfterSelf(new XText("\n" + childIndent), child);
            }
            else
            {
                var parentIndent = IndentOf(parent) ?? string.Empty;
                childIndent = parentIndent + indent;
                parent.Nodes().ToList().ForEach(n => n.Remove());
                parent.Add(new XText("\n" + childIndent), child, new XText("\n" + parentIndent));
            }

            FormatSubtree(child, childIndent, indent);
        }

        public static void RemoveElement(XElement element)
        {
            var parent = element.Parent!;
            if (element.PreviousNode is XText previous && IsWhitespace(previous) && previous.Value.IndexOf('\n') >= 0)
            {
                previous.Remove();
            }

            element.Remove();
            if (parent.Nodes().All(IsWhitespace))
            {
                // Nothing left but layout: collapse to <Parent />.
                parent.Nodes().ToList().ForEach(n => n.Remove());
            }
        }

        /// <summary>Indents a tree built in code, leaving any tree that already has text untouched.</summary>
        private static void FormatSubtree(XElement element, string elementIndent, string indent)
        {
            if (!element.Elements().Any() || element.Nodes().OfType<XText>().Any())
            {
                return;
            }

            var childIndent = elementIndent + indent;
            foreach (var node in element.Nodes().ToList())
            {
                node.AddBeforeSelf(new XText("\n" + childIndent));
                if (node is XElement child)
                {
                    FormatSubtree(child, childIndent, indent);
                }
            }

            element.Add(new XText("\n" + elementIndent));
        }

        /// <summary>Gets the indentation of the line a node starts on, if the node starts a line.</summary>
        private static string? IndentOf(XNode node)
        {
            if (node.PreviousNode is XText previous && IsWhitespace(previous))
            {
                var value = previous.Value;
                var newLine = value.LastIndexOf('\n');
                if (newLine >= 0)
                {
                    return value.Substring(newLine + 1);
                }
            }

            return node.Parent is null ? string.Empty : null;
        }

        private static bool IsWhitespace(XNode node) =>
            node is XText text && !(node is XCData) && text.Value.Trim(' ', '\t', '\r', '\n').Length == 0;
    }
}
