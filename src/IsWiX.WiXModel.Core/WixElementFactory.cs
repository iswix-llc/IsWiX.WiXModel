using System;
using System.Collections.Concurrent;
using System.Xml.Linq;

namespace IsWiX.WiXModel.Core
{
    /// <summary>
    /// Maps element names to typed wrappers. Model assemblies (the core model and each extension)
    /// register their element types once, from a module initializer.
    /// </summary>
    public static class WixElementFactory
    {
        private static readonly ConcurrentDictionary<XName, Func<XElement, WixElement>> Factories =
            new ConcurrentDictionary<XName, Func<XElement, WixElement>>();

        /// <summary>Registers the wrapper for an element name. Registering a name again has no effect.</summary>
        /// <param name="name">The element name.</param>
        /// <param name="create">Creates the wrapper around an existing element.</param>
        public static void Register(XName name, Func<XElement, WixElement> create)
        {
            if (name is null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            Factories.TryAdd(name, create ?? throw new ArgumentNullException(nameof(create)));
        }

        internal static bool IsRegistered(XName name) => Factories.ContainsKey(name);

        internal static WixElement Create(XElement element) =>
            Factories.TryGetValue(element.Name, out var create) ? create(element) : new WixUnknownElement(element);
    }
}
