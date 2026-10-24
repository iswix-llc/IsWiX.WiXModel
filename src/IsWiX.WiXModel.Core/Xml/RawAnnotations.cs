using System.Collections.Generic;
using System.Xml.Linq;

namespace IsWiX.WiXModel.Core.Xml
{
    // Annotations recording the exact source text of each node at load time. The writer reuses a node's
    // source text while the node's value still matches the value it had when it was loaded, and writes
    // fresh text only for what changed.

    /// <summary>Source text of a text, CDATA, comment or processing-instruction node.</summary>
    internal sealed class RawNode
    {
        public RawNode(string text, string valueAtLoad)
        {
            Text = text;
            ValueAtLoad = valueAtLoad;
        }

        public string Text { get; }

        public string ValueAtLoad { get; }
    }

    /// <summary>Whitespace before a document-level node (the XML declaration and root are not text nodes).</summary>
    internal sealed class RawGap
    {
        public RawGap(string text) => Text = text;

        public string Text { get; }
    }

    internal sealed class RawDocument
    {
        public RawDocument(string? declaration, string trailer)
        {
            Declaration = declaration;
            Trailer = trailer;
        }

        public string? Declaration { get; }

        public string Trailer { get; }
    }

    internal sealed class RawAttribute
    {
        public RawAttribute(XName name, string leadingWhitespace, string text, string rawName, char quote, string valueAtLoad)
        {
            Name = name;
            LeadingWhitespace = leadingWhitespace;
            Text = text;
            RawName = rawName;
            Quote = quote;
            ValueAtLoad = valueAtLoad;
        }

        public XName Name { get; }

        public string LeadingWhitespace { get; }

        /// <summary>Gets the whole <c>name="value"</c> text.</summary>
        public string Text { get; }

        public string RawName { get; }

        public char Quote { get; }

        public string ValueAtLoad { get; }
    }

    internal sealed class RawElement
    {
        public RawElement(XName name, string rawName, IReadOnlyList<RawAttribute> attributes, string tail, bool selfClosing, bool hadNodes, XNode? previousAtLoad)
        {
            Name = name;
            RawName = rawName;
            Attributes = attributes;
            Tail = tail;
            SelfClosing = selfClosing;
            HadNodes = hadNodes;
            PreviousAtLoad = previousAtLoad;
        }

        public XName Name { get; }

        public string RawName { get; }

        public IReadOnlyList<RawAttribute> Attributes { get; }

        /// <summary>Gets the end of the start tag: whitespace followed by <c>&gt;</c> or <c>/&gt;</c>.</summary>
        public string Tail { get; }

        public bool SelfClosing { get; }

        public bool HadNodes { get; }

        /// <summary>
        /// Gets the node that preceded the element when it was loaded. If the element's previous node is no longer
        /// this one, the element was moved (or something was inserted in front of it), and the writer lays it out
        /// like new content instead of trusting the whitespace around it.
        /// </summary>
        public XNode? PreviousAtLoad { get; }

        public string? EndTag { get; set; }
    }
}
