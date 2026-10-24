using System.IO;
using System.Xml.Linq;
using IsWiX.WiXModel.Core;
using IsWiX.WiXModel.Core.Xml;

namespace IsWiX.WiXModel
{
    /// <summary>
    /// A WiX v4+ include (.wxi) document, rooted at <c>&lt;Include&gt;</c>. Include files are pulled into
    /// .wxs files with <c>&lt;?include File.wxi?&gt;</c>; they usually hold <c>&lt;?define?&gt;</c> variables,
    /// and may hold any WiX elements. Saving keeps unchanged parts byte-for-byte, as with <see cref="WixDocument"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// var include = WixIncludeDocument.Load("Common.wxi");
    /// include.Root.Add(new Property { Id = "ARPNOMODIFY", Value = "1" });
    /// include.Save("Common.wxi");
    /// </code>
    /// </example>
    public sealed class WixIncludeDocument : WixSourceDocument
    {
        internal WixIncludeDocument(XDocument xml)
            : base(xml)
        {
            Root = (Include)WixElement.Wrap(RequireRoot(xml, Include.ElementName));
        }

        /// <summary>Gets the root <c>&lt;Include&gt;</c> element.</summary>
        public Include Root { get; }

        /// <summary>Creates an empty include file: an XML declaration and <c>&lt;Include xmlns="..."/&gt;</c>.</summary>
        /// <param name="format">How to write the document; <see langword="null"/> for <see cref="XmlTextFormat.Default"/>.</param>
        /// <returns>The document.</returns>
        public static WixIncludeDocument Create(XmlTextFormat? format = null) => new WixIncludeDocument(CreateXml(Include.ElementName, format));

        /// <summary>Loads a .wxi file.</summary>
        /// <param name="path">The file.</param>
        /// <returns>The document.</returns>
        /// <exception cref="WixDocumentException">The file is not a readable WiX v4+ include file.</exception>
        public static new WixIncludeDocument Load(string path)
        {
            using var stream = System.IO.File.OpenRead(path);
            return Load(stream);
        }

        /// <summary>Loads a .wxi document from a stream.</summary>
        /// <param name="stream">The stream, read to the end.</param>
        /// <returns>The document.</returns>
        /// <exception cref="WixDocumentException">The content is not a readable WiX v4+ include file.</exception>
        public static new WixIncludeDocument Load(Stream stream) => new WixIncludeDocument(ReadXml(() => LosslessXml.Load(stream)));

        /// <summary>Parses .wxi text.</summary>
        /// <param name="text">The include file's text.</param>
        /// <returns>The document.</returns>
        /// <exception cref="WixDocumentException">The text is not a WiX v4+ include document.</exception>
        public static new WixIncludeDocument Parse(string text) => new WixIncludeDocument(ReadXml(() => LosslessXml.Parse(text)));

        /// <summary>
        /// Wraps an <see cref="XDocument"/> already in memory without copying it. See <see cref="WixSourceDocument.FromXml"/>.
        /// </summary>
        /// <param name="xml">The document.</param>
        /// <returns>The typed document.</returns>
        /// <exception cref="WixDocumentException">The root is not <c>&lt;Include&gt;</c> in the WiX v4+ namespace.</exception>
        public static new WixIncludeDocument FromXml(XDocument xml) => new WixIncludeDocument(xml ?? throw new System.ArgumentNullException(nameof(xml)));
    }
}
