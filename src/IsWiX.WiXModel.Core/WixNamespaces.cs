using System;
using System.Collections.Generic;
using System.Linq;
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

        private static readonly char[] NamespaceSeparators = { '/', ':', '#', '.' };

        /// <summary>
        /// The extensions WiX ships and the prefixes WiX's own sources use for them (WiX v7.0.0, src/ext).
        /// </summary>
        public static IReadOnlyDictionary<XNamespace, string> ExtensionPrefixes { get; } = new Dictionary<XNamespace, string>
        {
            [Extension("bal")] = "bal",
            [Extension("complus")] = "complus",
            [Extension("dependency")] = "dep",
            [Extension("directx")] = "directx",
            [Extension("firewall")] = "fw",
            [Extension("http")] = "http",
            [Extension("iis")] = "iis",
            [Extension("msmq")] = "msmq",
            [Extension("netfx")] = "netfx",
            [Extension("powershell")] = "powershell",
            [Extension("sql")] = "sql",
            [Extension("ui")] = "ui",
            [Extension("util")] = "util",
            [Extension("vs")] = "vs",
        };

        /// <summary>Gets a WiX extension's namespace, such as <c>http://wixtoolset.org/schemas/v4/wxs/util</c>.</summary>
        /// <param name="name">The extension's name in its namespace (<c>util</c>, <c>netfx</c>, ...).</param>
        /// <returns>The namespace.</returns>
        public static XNamespace Extension(string name) => XNamespace.Get(Wxs.NamespaceName + "/" + name);

        /// <summary>
        /// Makes sure <paramref name="ns"/> has a prefix where <paramref name="element"/> is, declaring
        /// <c>xmlns:prefix</c> on the document's root element when it has none. Elements and attributes added
        /// through the model do this themselves; call it for XML added any other way.
        /// </summary>
        /// <param name="element">An element in the document (or tree) that uses the namespace.</param>
        /// <param name="ns">The namespace.</param>
        /// <returns>The prefix in scope at <paramref name="element"/>; empty when it is the default namespace.</returns>
        public static string Declare(XElement element, XNamespace ns)
        {
            if (element is null)
            {
                throw new ArgumentNullException(nameof(element));
            }

            if (ns is null || ns == XNamespace.None || ns == XNamespace.Xml || ns == XNamespace.Xmlns)
            {
                return string.Empty;
            }

            if (element.GetDefaultNamespace() == ns)
            {
                return string.Empty;
            }

            var existing = element.GetPrefixOfNamespace(ns);
            if (existing != null)
            {
                return existing;
            }

            var root = element.AncestorsAndSelf().Last();
            var prefix = PreferredPrefix(ns);
            var candidate = prefix;
            for (var n = 2; root.Attribute(XNamespace.Xmlns + candidate) != null || IsInUse(root, candidate); n++)
            {
                candidate = prefix + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            root.Add(new XAttribute(XNamespace.Xmlns + candidate, ns.NamespaceName));
            return candidate;
        }

        /// <summary>
        /// Declares every namespace <paramref name="element"/> and its descendants use (element and attribute
        /// names) that has no prefix yet.
        /// </summary>
        /// <param name="element">The element.</param>
        public static void DeclareAll(XElement element)
        {
            if (element is null)
            {
                throw new ArgumentNullException(nameof(element));
            }

            foreach (var e in element.DescendantsAndSelf().ToList())
            {
                Declare(e, e.Name.Namespace);
                foreach (var attribute in e.Attributes().Where(a => !a.IsNamespaceDeclaration).ToList())
                {
                    Declare(e, attribute.Name.Namespace);
                }
            }
        }

        /// <summary>
        /// The prefix a new declaration of <paramref name="ns"/> gets: the WiX extension's usual prefix, else the
        /// last word of the namespace (<c>urn:acme:custom</c> is <c>custom</c>), else <c>ext</c>.
        /// </summary>
        /// <param name="ns">The namespace.</param>
        /// <returns>The prefix.</returns>
        public static string PreferredPrefix(XNamespace ns)
        {
            if (ns is null)
            {
                throw new ArgumentNullException(nameof(ns));
            }

            if (ExtensionPrefixes.TryGetValue(ns, out var known))
            {
                return known;
            }

            // The words after the scheme (urn:, http:), last first.
            var name = ns.NamespaceName;
            var colon = name.IndexOf(':');
            var words = (colon > 0 ? name.Substring(colon + 1) : name).Split(NamespaceSeparators, StringSplitOptions.RemoveEmptyEntries);
            var last = Enumerable.Reverse(words)
                .Select(w => new string(w.Where(char.IsLetterOrDigit).ToArray()))
                .FirstOrDefault(w => w.Length > 0 && char.IsLetter(w[0]) && !w.StartsWith("xml", StringComparison.OrdinalIgnoreCase));
            return last?.ToLowerInvariant() ?? "ext";
        }

        // A prefix declared anywhere below the root for another namespace can't be reused on the root without
        // changing what it means there.
        private static bool IsInUse(XElement root, string prefix) =>
            root.DescendantsAndSelf().Any(e => e.Attribute(XNamespace.Xmlns + prefix) != null);
    }
}
