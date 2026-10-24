using System;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using IsWiX.WiXModel.Core;
using IsWiX.WiXModel.Core.Xml;

namespace IsWiX.WiXModel
{
    /// <summary>
    /// A WiX v4+ source file: a <see cref="WixDocument"/> (.wxs, rooted at <c>&lt;Wix&gt;</c>) or a
    /// <see cref="WixIncludeDocument"/> (.wxi, rooted at <c>&lt;Include&gt;</c>). Unchanged parts of a loaded file
    /// are saved byte-for-byte; edits change only what they touch and follow the file's own line endings and
    /// indentation.
    /// </summary>
    public abstract class WixSourceDocument
    {
        private protected WixSourceDocument(XDocument xml) => Xml = xml;

        /// <summary>Gets the underlying XML.</summary>
        public XDocument Xml { get; }

        /// <summary>Gets the root element, typed by the derived document.</summary>
        public WixElement RootElement => WixElement.Wrap(Xml.Root!);

        /// <summary>Gets the encoding, line endings and indentation the document is saved with.</summary>
        public XmlTextFormat Format => LosslessXml.GetFormat(Xml);

        /// <summary>
        /// Gets a value indicating whether unchanged parts of the source are saved byte-for-byte.
        /// See <see cref="LosslessXml.IsLossless"/>.
        /// </summary>
        public bool IsLossless => LosslessXml.IsLossless(Xml);

        /// <summary>
        /// Loads a .wxs or .wxi file, returning a <see cref="WixDocument"/> or a <see cref="WixIncludeDocument"/>
        /// depending on its root element. Use this when the kind of file is not known in advance.
        /// </summary>
        /// <param name="path">The file.</param>
        /// <returns>The document.</returns>
        /// <exception cref="WixDocumentException">The file is not a readable WiX v4+ source or include file.</exception>
        public static WixSourceDocument Load(string path)
        {
            using var stream = System.IO.File.OpenRead(path);
            return Load(stream);
        }

        /// <summary>Loads a .wxs or .wxi document from a stream. See <see cref="Load(string)"/>.</summary>
        /// <param name="stream">The stream, read to the end.</param>
        /// <returns>The document.</returns>
        /// <exception cref="WixDocumentException">The content is not a readable WiX v4+ source or include file.</exception>
        public static WixSourceDocument Load(Stream stream) => FromXmlCore(ReadXml(() => LosslessXml.Load(stream)));

        /// <summary>Parses .wxs or .wxi text. See <see cref="Load(string)"/>.</summary>
        /// <param name="text">The WiX source.</param>
        /// <returns>The document.</returns>
        /// <exception cref="WixDocumentException">The text is not a WiX v4+ source or include document.</exception>
        public static WixSourceDocument Parse(string text) => FromXmlCore(ReadXml(() => LosslessXml.Parse(text)));

        /// <summary>
        /// Saves to a file. The file is written to a temporary file first and then swapped in, so a failure
        /// part-way through never leaves a truncated source file.
        /// </summary>
        /// <param name="path">The destination.</param>
        public void Save(string path) => LosslessXml.Save(Xml, path);

        /// <summary>
        /// Wraps an <see cref="XDocument"/> that is already in memory, such as one a host application owns, as a
        /// <see cref="WixDocument"/> or <see cref="WixIncludeDocument"/> depending on its root element. The
        /// document is not copied: edits through the model and edits to <paramref name="xml"/> are the same edits.
        /// </summary>
        /// <remarks>
        /// Load the <see cref="XDocument"/> with <see cref="LosslessXml.Load(Stream)"/> to keep unchanged parts
        /// byte-for-byte on save. A document loaded any other way still works, but is saved with normalized
        /// markup (<see cref="IsLossless"/> is <see langword="false"/>).
        /// </remarks>
        /// <param name="xml">The document.</param>
        /// <returns>The typed document.</returns>
        /// <exception cref="WixDocumentException">The document is not a WiX v4+ source or include document.</exception>
        public static WixSourceDocument FromXml(XDocument xml) => FromXmlCore(xml ?? throw new ArgumentNullException(nameof(xml)));

        /// <summary>Saves to a stream.</summary>
        /// <param name="stream">The destination.</param>
        public void Save(Stream stream) => LosslessXml.Save(Xml, stream);

        /// <summary>Gets the document's text, as <see cref="Save(Stream)"/> would write it.</summary>
        /// <returns>The XML text.</returns>
        public override string ToString() => LosslessXml.ToXmlString(Xml);

        private protected static XDocument ReadXml(Func<XDocument> read)
        {
            try
            {
                return read();
            }
            catch (Exception ex) when (ex is XmlException || ex is InvalidDataException)
            {
                throw new WixDocumentException(ex.Message, ex);
            }
        }

        private protected static XDocument CreateXml(XName root, XmlTextFormat? format)
        {
            var xml = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement(root, new XAttribute("xmlns", WixNamespaces.Wxs.NamespaceName)));
            LosslessXml.SetFormat(xml, format ?? XmlTextFormat.Default);
            return xml;
        }

        /// <summary>Checks the root element, explaining what the file is when it is not what was asked for.</summary>
        private protected static XElement RequireRoot(XDocument xml, XName expected)
        {
            var root = xml.Root ?? throw new WixDocumentException("The document has no root element.");
            if (root.Name == expected)
            {
                return root;
            }

            if (root.Name.Namespace == WixNamespaces.WixV3)
            {
                throw new WixDocumentException("This is a WiX v3 source file. Convert it to WiX v4+ with 'wix convert' before loading it.");
            }

            if (root.Name == Include.ElementName)
            {
                throw new WixDocumentException("This is a WiX include (.wxi) file. Load it with WixIncludeDocument or WixSourceDocument.");
            }

            if (root.Name == Wix.ElementName)
            {
                throw new WixDocumentException("This is a WiX source (.wxs) file. Load it with WixDocument or WixSourceDocument.");
            }

            throw new WixDocumentException(
                $"The root element is <{root.Name.LocalName}> in namespace '{root.Name.NamespaceName}', not <{expected.LocalName}> in '{WixNamespaces.Wxs.NamespaceName}'.");
        }

        private static WixSourceDocument FromXmlCore(XDocument xml) =>
            xml.Root?.Name == Include.ElementName ? new WixIncludeDocument(xml) : new WixDocument(xml);
    }
}
