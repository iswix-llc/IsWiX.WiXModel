using System;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using IsWiX.WiXModel.Core;
using IsWiX.WiXModel.Core.Xml;

namespace IsWiX.WiXModel
{
    /// <summary>
    /// A WiX v4+ source (.wxs) document. Unchanged parts of a loaded file are saved byte-for-byte; edits
    /// change only what they touch and follow the file's own line endings and indentation.
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
    public sealed class WixDocument
    {
        private WixDocument(XDocument xml)
        {
            var root = xml.Root ?? throw new WixDocumentException("The document has no root element.");
            if (root.Name != Wix.ElementName)
            {
                throw new WixDocumentException(root.Name.Namespace == WixNamespaces.WixV3
                    ? "This is a WiX v3 source file. Convert it to WiX v4+ with 'wix convert' before loading it."
                    : $"The root element is <{root.Name.LocalName}> in namespace '{root.Name.NamespaceName}', not <Wix> in '{WixNamespaces.Wxs.NamespaceName}'.");
            }

            Xml = xml;
            Root = (Wix)WixElement.Wrap(root);
        }

        /// <summary>Gets the underlying XML.</summary>
        public XDocument Xml { get; }

        /// <summary>Gets the root <c>&lt;Wix&gt;</c> element.</summary>
        public Wix Root { get; }

        /// <summary>Gets the encoding, line endings and indentation the document is saved with.</summary>
        public XmlTextFormat Format => LosslessXml.GetFormat(Xml);

        /// <summary>
        /// Gets a value indicating whether unchanged parts of the source are saved byte-for-byte.
        /// See <see cref="LosslessXml.IsLossless"/>.
        /// </summary>
        public bool IsLossless => LosslessXml.IsLossless(Xml);

        /// <summary>Creates an empty document: an XML declaration and <c>&lt;Wix xmlns="..."/&gt;</c>.</summary>
        /// <param name="format">How to write the document; <see langword="null"/> for <see cref="XmlTextFormat.Default"/>.</param>
        /// <returns>The document.</returns>
        public static WixDocument Create(XmlTextFormat? format = null)
        {
            var xml = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement(Wix.ElementName, new XAttribute("xmlns", WixNamespaces.Wxs.NamespaceName)));
            LosslessXml.SetFormat(xml, format ?? XmlTextFormat.Default);
            return new WixDocument(xml);
        }

        /// <summary>Loads a file.</summary>
        /// <param name="path">The .wxs file.</param>
        /// <returns>The document.</returns>
        /// <exception cref="WixDocumentException">The file is not a readable WiX v4+ source file.</exception>
        public static WixDocument Load(string path)
        {
            using var stream = System.IO.File.OpenRead(path);
            return Load(stream);
        }

        /// <summary>Loads a document from a stream.</summary>
        /// <param name="stream">The stream, read to the end.</param>
        /// <returns>The document.</returns>
        /// <exception cref="WixDocumentException">The content is not a readable WiX v4+ source file.</exception>
        public static WixDocument Load(Stream stream)
        {
            try
            {
                return new WixDocument(LosslessXml.Load(stream));
            }
            catch (Exception ex) when (ex is XmlException || ex is InvalidDataException)
            {
                throw new WixDocumentException(ex.Message, ex);
            }
        }

        /// <summary>Parses text.</summary>
        /// <param name="text">The WiX source.</param>
        /// <returns>The document.</returns>
        /// <exception cref="WixDocumentException">The text is not a WiX v4+ source document.</exception>
        public static WixDocument Parse(string text)
        {
            try
            {
                return new WixDocument(LosslessXml.Parse(text));
            }
            catch (XmlException ex)
            {
                throw new WixDocumentException(ex.Message, ex);
            }
        }

        /// <summary>
        /// Saves to a file. The file is written to a temporary file first and then swapped in, so a failure
        /// part-way through never leaves a truncated source file.
        /// </summary>
        /// <param name="path">The destination.</param>
        public void Save(string path)
        {
            var fullPath = Path.GetFullPath(path);
            var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = System.IO.File.Create(temporary))
                {
                    Save(stream);
                }

                if (System.IO.File.Exists(fullPath))
                {
                    System.IO.File.Replace(temporary, fullPath, destinationBackupFileName: null);
                }
                else
                {
                    System.IO.File.Move(temporary, fullPath);
                }
            }
            finally
            {
                if (System.IO.File.Exists(temporary))
                {
                    System.IO.File.Delete(temporary);
                }
            }
        }

        /// <summary>Saves to a stream.</summary>
        /// <param name="stream">The destination.</param>
        public void Save(Stream stream) => LosslessXml.Save(Xml, stream);

        /// <summary>Gets the document's text, as <see cref="Save(Stream)"/> would write it.</summary>
        /// <returns>The XML text.</returns>
        public override string ToString() => LosslessXml.ToXmlString(Xml);
    }
}
