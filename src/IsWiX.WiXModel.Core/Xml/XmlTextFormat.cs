using System;
using System.Text;

namespace IsWiX.WiXModel.Core.Xml
{
    /// <summary>
    /// How a document is written as text: encoding, byte order mark, line endings and the indentation used
    /// for new content. Loading a file detects these so that edits match the existing file.
    /// </summary>
    public sealed class XmlTextFormat
    {
        /// <summary>Creates a format.</summary>
        /// <param name="encoding">UTF-8 or UTF-16. The encoding's preamble decides whether a byte order mark is written.</param>
        /// <param name="newLine"><c>"\r\n"</c> or <c>"\n"</c>.</param>
        /// <param name="indent">One level of indentation, such as four spaces or a tab.</param>
        public XmlTextFormat(Encoding encoding, string newLine, string indent)
        {
            Encoding = encoding ?? throw new ArgumentNullException(nameof(encoding));
            if (newLine != "\r\n" && newLine != "\n")
            {
                throw new ArgumentException("New line must be \"\\r\\n\" or \"\\n\".", nameof(newLine));
            }

            if (string.IsNullOrEmpty(indent) || indent.Trim(' ', '\t').Length != 0)
            {
                throw new ArgumentException("Indent must be spaces or tabs.", nameof(indent));
            }

            NewLine = newLine;
            Indent = indent;
        }

        /// <summary>
        /// Gets the format for new documents: UTF-8 without a byte order mark, CRLF line endings
        /// (WiX targets Windows) and four-space indentation.
        /// </summary>
        public static XmlTextFormat Default { get; } = new XmlTextFormat(new UTF8Encoding(false), "\r\n", "    ");

        /// <summary>Gets the encoding.</summary>
        public Encoding Encoding { get; }

        /// <summary>Gets the line ending used for new content.</summary>
        public string NewLine { get; }

        /// <summary>Gets one level of indentation used for new content.</summary>
        public string Indent { get; }

        /// <summary>Gets a value indicating whether a byte order mark is written.</summary>
        public bool HasByteOrderMark => Encoding.GetPreamble().Length > 0;
    }
}
