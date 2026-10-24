using System;

namespace IsWiX.WiXModel
{
    /// <summary>A file could not be loaded as a WiX v4+ source document.</summary>
    public sealed class WixDocumentException : Exception
    {
        /// <summary>Creates the exception.</summary>
        public WixDocumentException()
        {
        }

        /// <summary>Creates the exception.</summary>
        /// <param name="message">What went wrong.</param>
        public WixDocumentException(string message)
            : base(message)
        {
        }

        /// <summary>Creates the exception.</summary>
        /// <param name="message">What went wrong.</param>
        /// <param name="innerException">The underlying error.</param>
        public WixDocumentException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
