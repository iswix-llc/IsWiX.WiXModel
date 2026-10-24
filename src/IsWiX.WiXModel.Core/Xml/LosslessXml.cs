using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace IsWiX.WiXModel.Core.Xml
{
    /// <summary>
    /// Loads and saves XML so that saving an unmodified document reproduces the original bytes, and saving
    /// a modified one changes only what was edited. <see cref="XDocument"/> alone cannot do this: it
    /// normalizes quotes, whitespace inside tags, entity references and line endings.
    /// </summary>
    public static class LosslessXml
    {
        /// <summary>Loads a document, detecting its encoding, line endings and indentation.</summary>
        /// <param name="stream">The stream to read to the end.</param>
        /// <returns>The document.</returns>
        /// <exception cref="InvalidDataException">The bytes are not valid UTF-8 or UTF-16.</exception>
        /// <exception cref="XmlException">The text is not well-formed XML.</exception>
        public static XDocument Load(Stream stream)
        {
            if (stream is null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var bytes = buffer.ToArray();

            var encoding = DetectEncoding(bytes, out var preambleLength);
            string text;
            try
            {
                text = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
            }
            catch (DecoderFallbackException ex)
            {
                throw new InvalidDataException("The file is not valid UTF-8 or UTF-16. Re-save it as UTF-8.", ex);
            }

            return Parse(text, encoding);
        }

        /// <summary>Parses text. The document is saved as UTF-8 without a byte order mark unless changed with <see cref="SetFormat"/>.</summary>
        /// <param name="text">The XML text.</param>
        /// <returns>The document.</returns>
        /// <exception cref="XmlException">The text is not well-formed XML.</exception>
        public static XDocument Parse(string text) => Parse(text ?? throw new ArgumentNullException(nameof(text)), new UTF8Encoding(false));

        /// <summary>Saves a document using its <see cref="GetFormat">format</see>.</summary>
        /// <param name="document">The document.</param>
        /// <param name="stream">The destination.</param>
        public static void Save(XDocument document, Stream stream)
        {
            if (stream is null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            var format = GetFormat(document);
            var preamble = format.Encoding.GetPreamble();
            stream.Write(preamble, 0, preamble.Length);
            var bytes = format.Encoding.GetBytes(ToXmlString(document));
            stream.Write(bytes, 0, bytes.Length);
        }

        /// <summary>Gets the document's text, as <see cref="Save"/> would write it.</summary>
        /// <param name="document">The document.</param>
        /// <returns>The XML text.</returns>
        public static string ToXmlString(XDocument document) =>
            RawWriter.Write(document ?? throw new ArgumentNullException(nameof(document)), GetFormat(document).NewLine);

        /// <summary>Gets the format the document is saved with.</summary>
        /// <param name="document">The document.</param>
        /// <returns>The detected format for a loaded document, otherwise <see cref="XmlTextFormat.Default"/>.</returns>
        public static XmlTextFormat GetFormat(XDocument document) =>
            (document ?? throw new ArgumentNullException(nameof(document))).Annotation<XmlTextFormat>() ?? XmlTextFormat.Default;

        /// <summary>Sets the format the document is saved with.</summary>
        /// <param name="document">The document.</param>
        /// <param name="format">The format.</param>
        public static void SetFormat(XDocument document, XmlTextFormat format)
        {
            if (document is null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            document.RemoveAnnotations<XmlTextFormat>();
            document.AddAnnotation(format ?? throw new ArgumentNullException(nameof(format)));
        }

        /// <summary>
        /// Gets a value indicating whether the document was loaded with its source text captured, so that
        /// unchanged parts are saved byte-for-byte. It is <see langword="false"/> for documents built in code,
        /// and for loaded documents using XML features WiX files do not use (such as a DTD), which are saved
        /// with normalized markup instead.
        /// </summary>
        /// <param name="document">The document.</param>
        /// <returns><see langword="true"/> when saving preserves the source text.</returns>
        public static bool IsLossless(XDocument document) =>
            (document ?? throw new ArgumentNullException(nameof(document))).Annotation<RawDocument>() != null;

        private static XDocument Parse(string text, Encoding encoding)
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
            };

            XDocument document;
            using (var reader = XmlReader.Create(new StringReader(text), settings))
            {
                document = XDocument.Load(reader, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
            }

            var newLine = DetectNewLine(text);
            SetFormat(document, new XmlTextFormat(encoding, newLine, DetectIndent(text) ?? XmlTextFormat.Default.Indent));

            try
            {
                RawCapture.Capture(document, text);

                // Belt and braces: the capture is only kept if it reproduces the source exactly.
                if (!string.Equals(RawWriter.Write(document, newLine), text, StringComparison.Ordinal))
                {
                    RawCapture.Clear(document);
                }
            }
            catch (RawCaptureException)
            {
                RawCapture.Clear(document);
            }

            return document;
        }

        private static Encoding DetectEncoding(byte[] bytes, out int preambleLength)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                preambleLength = 3;
                return new UTF8Encoding(true, true);
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                preambleLength = 2;
                return new UnicodeEncoding(false, true, true);
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                preambleLength = 2;
                return new UnicodeEncoding(true, true, true);
            }

            preambleLength = 0;
            return new UTF8Encoding(false, true);
        }

        private static string DetectNewLine(string text)
        {
            var crlf = 0;
            var lf = 0;
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    if (i > 0 && text[i - 1] == '\r')
                    {
                        crlf++;
                    }
                    else
                    {
                        lf++;
                    }
                }
            }

            return crlf == 0 && lf == 0 ? XmlTextFormat.Default.NewLine : (crlf >= lf ? "\r\n" : "\n");
        }

        /// <summary>Finds one level of indentation: a tab, or the smallest run of spaces before a tag.</summary>
        private static string? DetectIndent(string text)
        {
            var smallest = int.MaxValue;
            foreach (var line in text.Split('\n').Select(l => l.TrimEnd('\r')))
            {
                var content = line.TrimStart(' ', '\t');
                if (content.Length == line.Length || !content.StartsWith("<", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line[0] == '\t')
                {
                    return "\t";
                }

                smallest = Math.Min(smallest, line.Length - content.Length);
            }

            return smallest == int.MaxValue ? null : new string(' ', smallest);
        }
    }
}
