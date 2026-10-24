using System.IO;
using System.Xml.Linq;
using IsWiX.WiXModel.Core;
using IsWiX.WiXModel.Core.Xml;

namespace IsWiX.WiXModel
{
    /// <summary>
    /// A WiX v4+ source (.wxs) document, rooted at <c>&lt;Wix&gt;</c>. Unchanged parts of a loaded file are
    /// saved byte-for-byte; edits change only what they touch and follow the file's own line endings and
    /// indentation.
    /// </summary>
    /// <example>
    /// <code>
    /// var document = WixDocument.Load("Product.wxs");
    /// foreach (var component in document.Root.Packages.Single().Components)
    /// {
    ///     component.Bitness = BitnessType.Always64;
    /// }
    /// document.Save("Product.wxs");
    /// </code>
    /// </example>
    public sealed class WixDocument : WixSourceDocument
    {
        internal WixDocument(XDocument xml)
            : base(xml)
        {
            Root = (Wix)WixElement.Wrap(RequireRoot(xml, Wix.ElementName));
        }

        /// <summary>Gets the root <c>&lt;Wix&gt;</c> element.</summary>
        public Wix Root { get; }

        /// <summary>Creates an empty document: an XML declaration and <c>&lt;Wix xmlns="..."/&gt;</c>.</summary>
        /// <param name="format">How to write the document; <see langword="null"/> for <see cref="XmlTextFormat.Default"/>.</param>
        /// <returns>The document.</returns>
        public static WixDocument Create(XmlTextFormat? format = null) => new WixDocument(CreateXml(Wix.ElementName, format));

        /// <summary>Loads a .wxs file.</summary>
        /// <param name="path">The file.</param>
        /// <returns>The document.</returns>
        /// <exception cref="WixDocumentException">The file is not a readable WiX v4+ source file.</exception>
        public static new WixDocument Load(string path)
        {
            using var stream = System.IO.File.OpenRead(path);
            return Load(stream);
        }

        /// <summary>Loads a .wxs document from a stream.</summary>
        /// <param name="stream">The stream, read to the end.</param>
        /// <returns>The document.</returns>
        /// <exception cref="WixDocumentException">The content is not a readable WiX v4+ source file.</exception>
        public static new WixDocument Load(Stream stream) => new WixDocument(ReadXml(() => LosslessXml.Load(stream)));

        /// <summary>Parses .wxs text.</summary>
        /// <param name="text">The WiX source.</param>
        /// <returns>The document.</returns>
        /// <exception cref="WixDocumentException">The text is not a WiX v4+ source document.</exception>
        public static new WixDocument Parse(string text) => new WixDocument(ReadXml(() => LosslessXml.Parse(text)));
    }
}
