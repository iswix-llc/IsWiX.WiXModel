using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using IsWiX.WiXModel.Core.Xml;

namespace IsWiX.WiXModel.Core
{
    /// <summary>
    /// Base class for typed views over WiX elements. A wrapper holds no state of its own: every property
    /// reads and writes the underlying <see cref="XElement"/>, so the XML stays the single source of truth
    /// and anything the model does not cover (comments, preprocessor instructions, extension attributes)
    /// is kept untouched.
    /// </summary>
    public abstract class WixElement
    {
        /// <summary>Wraps <paramref name="xml"/> and registers this wrapper as its canonical view.</summary>
        /// <param name="xml">The element to wrap.</param>
        protected WixElement(XElement xml)
        {
            Xml = xml ?? throw new ArgumentNullException(nameof(xml));
            xml.RemoveAnnotations<WixElement>();
            xml.AddAnnotation(this);
        }

        /// <summary>Gets the underlying element. Use it for anything the typed model does not cover.</summary>
        public XElement Xml { get; }

        /// <summary>Gets the parent element, or <see langword="null"/> for the root or a detached element.</summary>
        public WixElement? Parent => Xml.Parent is null ? null : Wrap(Xml.Parent);

        /// <summary>Gets all child elements, typed where a wrapper is registered.</summary>
        public IEnumerable<WixElement> Children => Xml.Elements().Select(Wrap);

        /// <summary>
        /// Gets the wrapper for <paramref name="element"/>. The same instance is returned every time, so wrappers
        /// can be compared by reference.
        /// </summary>
        /// <param name="element">The element.</param>
        /// <returns>The typed wrapper, or a <see cref="WixUnknownElement"/> when no wrapper is registered.</returns>
        public static WixElement Wrap(XElement element)
        {
            if (element is null)
            {
                throw new ArgumentNullException(nameof(element));
            }

            var existing = element.Annotation<WixElement>();
            if (existing != null && !(existing is WixUnknownElement && WixElementFactory.IsRegistered(element.Name)))
            {
                return existing;
            }

            return WixElementFactory.Create(element);
        }

        /// <summary>Gets an attribute's text, including attributes from extension namespaces.</summary>
        /// <param name="name">The attribute name.</param>
        /// <returns>The text, or <see langword="null"/> when the attribute is absent.</returns>
        public string? GetAttributeValue(XName name) => Xml.Attribute(name)?.Value;

        /// <summary>
        /// Sets an attribute's text, keeping its position. <see langword="null"/> removes it. An attribute in an
        /// extension namespace that has no prefix yet gets one declared on the root element.
        /// </summary>
        /// <param name="name">The attribute name.</param>
        /// <param name="value">The text.</param>
        public void SetAttributeValue(XName name, string? value)
        {
            Xml.SetAttributeValue(name, value);
            if (value != null && Xml.Document != null)
            {
                WixNamespaces.Declare(Xml, name.Namespace);
            }
        }

        /// <summary>
        /// Adds any element as a child, such as one from a WiX extension that has no typed wrapper. It is placed
        /// and indented like elements added through the typed model, and its namespace (and its attributes')
        /// gets a prefix declared on the root element if it has none.
        /// </summary>
        /// <param name="element">A detached element.</param>
        /// <returns>The element's wrapper.</returns>
        /// <exception cref="InvalidOperationException"><paramref name="element"/> already has a parent.</exception>
        public WixElement AddElement(XElement element)
        {
            if (element is null)
            {
                throw new ArgumentNullException(nameof(element));
            }

            return AddChild(Wrap(element));
        }

        /// <summary>
        /// Removes this element from its parent, along with the line it occupied, so that the surrounding
        /// layout is unchanged.
        /// </summary>
        /// <remarks>
        /// Not named <c>Remove</c>: WiX attributes such as <c>ServiceControl/@Remove</c> need that name.
        /// </remarks>
        public void RemoveFromParent()
        {
            if (Xml.Parent is null)
            {
                throw new InvalidOperationException($"<{Xml.Name.LocalName}> has no parent to be removed from.");
            }

            XmlLayout.RemoveElement(Xml);
        }

        /// <summary>Gets a string attribute.</summary>
        /// <param name="name">The attribute name.</param>
        /// <returns>The text, or <see langword="null"/>.</returns>
        protected string? GetString(XName name) => Xml.Attribute(name)?.Value;

        /// <summary>Sets or removes a string attribute.</summary>
        /// <param name="name">The attribute name.</param>
        /// <param name="value">The text; <see langword="null"/> removes the attribute.</param>
        protected void SetString(XName name, string? value) => SetAttributeValue(name, value);

        /// <summary>Gets a typed attribute.</summary>
        /// <typeparam name="T">The literal type.</typeparam>
        /// <param name="name">The attribute name.</param>
        /// <param name="converter">The attribute's converter.</param>
        /// <returns>The value, or <see langword="null"/> when the attribute is absent.</returns>
        protected WixValue<T>? GetValue<T>(XName name, WixValueConverter<T> converter)
        {
            var attribute = Xml.Attribute(name);
            return attribute is null ? (WixValue<T>?)null : converter.Parse(attribute.Value);
        }

        /// <summary>Sets or removes a typed attribute.</summary>
        /// <typeparam name="T">The literal type.</typeparam>
        /// <param name="name">The attribute name.</param>
        /// <param name="converter">The attribute's converter.</param>
        /// <param name="value">The value; <see langword="null"/> removes the attribute.</param>
        protected void SetValue<T>(XName name, WixValueConverter<T> converter, WixValue<T>? value) =>
            SetAttributeValue(name, value.HasValue ? converter.ToText(value.Value) : null);

        /// <summary>Gets the child elements with a given name.</summary>
        /// <typeparam name="T">The wrapper type registered for <paramref name="name"/>.</typeparam>
        /// <param name="name">The element name.</param>
        /// <returns>The children in document order.</returns>
        protected IEnumerable<T> GetChildren<T>(XName name)
            where T : WixElement => Xml.Elements(name).Select(Wrap).OfType<T>();

        /// <summary>
        /// Adds a detached element as a child. It goes after the last sibling of the same kind (or at the end),
        /// indented to match its siblings.
        /// </summary>
        /// <typeparam name="T">The child type.</typeparam>
        /// <param name="child">The child to add.</param>
        /// <returns><paramref name="child"/>, for chaining.</returns>
        /// <exception cref="InvalidOperationException"><paramref name="child"/> already has a parent.</exception>
        protected T AddChild<T>(T child)
            where T : WixElement
        {
            if (child is null)
            {
                throw new ArgumentNullException(nameof(child));
            }

            // XContainer.Add silently clones an element that already has a parent; a wrapper would then
            // point at the original, so moving must be explicit.
            if (child.Xml.Parent != null || child.Xml.Document != null)
            {
                throw new InvalidOperationException($"<{child.Xml.Name.LocalName}> already has a parent. Call RemoveFromParent() before adding it elsewhere.");
            }

            if (child.Xml == Xml || child.Xml.Descendants().Contains(Xml))
            {
                throw new InvalidOperationException("An element cannot be added to itself or to one of its descendants.");
            }

            XmlLayout.InsertElement(Xml, child.Xml);
            return child;
        }

        /// <inheritdoc/>
        public override string ToString() => Xml.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>A view over an element that has no typed wrapper, such as one from an unregistered extension.</summary>
    public sealed class WixUnknownElement : WixElement
    {
        internal WixUnknownElement(XElement xml)
            : base(xml)
        {
        }
    }
}
