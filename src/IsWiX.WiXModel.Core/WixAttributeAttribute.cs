using System;
using System.Xml.Linq;

namespace IsWiX.WiXModel.Core
{
    /// <summary>
    /// Names the XML attribute a generated property reads and writes, when the property is not simply named after
    /// it: an extension attribute on a core element, such as <c>Variable.BalOverridable</c> for
    /// <c>bal:Overridable</c>. Properties without it are named after an attribute in no namespace.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public sealed class WixAttributeAttribute : Attribute
    {
        /// <summary>Initializes a new instance of the <see cref="WixAttributeAttribute"/> class.</summary>
        /// <param name="namespaceName">The attribute's namespace.</param>
        /// <param name="localName">The attribute's local name.</param>
        public WixAttributeAttribute(string namespaceName, string localName)
        {
            NamespaceName = namespaceName;
            LocalName = localName;
        }

        /// <summary>Gets the attribute's namespace.</summary>
        public string NamespaceName { get; }

        /// <summary>Gets the attribute's local name.</summary>
        public string LocalName { get; }

        /// <summary>Gets the attribute's XML name.</summary>
        public XName Name => XNamespace.Get(NamespaceName) + LocalName;
    }
}
