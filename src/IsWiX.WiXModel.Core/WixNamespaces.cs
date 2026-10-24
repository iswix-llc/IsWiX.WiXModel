using System.Xml.Linq;

namespace IsWiX.WiXModel.Core
{
    /// <summary>XML namespaces used by WiX source files.</summary>
    public static class WixNamespaces
    {
        /// <summary>The core namespace used by WiX v4 and later (v4 through v7 share it).</summary>
        public static readonly XNamespace Wxs = "http://wixtoolset.org/schemas/v4/wxs";

        /// <summary>The WiX v3 namespace. Files in it must be converted with <c>wix convert</c> before loading.</summary>
        public static readonly XNamespace WixV3 = "http://schemas.microsoft.com/wix/2006/wi";
    }
}
