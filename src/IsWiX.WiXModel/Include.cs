using IsWiX.WiXModel.Core;

namespace IsWiX.WiXModel
{
    /// <content>
    /// The schema lets <c>&lt;Include&gt;</c> hold any element, so it has no typed child collections; read its
    /// contents through <see cref="WixElement.Children"/> (for example <c>Children.OfType&lt;Property&gt;()</c>).
    /// </content>
    public sealed partial class Include
    {
        /// <summary>Adds any element after the last existing one of the same kind, indented to match.</summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="child">A detached element.</param>
        /// <returns><paramref name="child"/>, for chaining.</returns>
        public T Add<T>(T child)
            where T : WixElement => AddChild(child);
    }
}
