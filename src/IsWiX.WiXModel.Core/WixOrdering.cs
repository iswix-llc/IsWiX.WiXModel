using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace IsWiX.WiXModel.Core
{
    /// <summary>
    /// Rules that put child elements in a canonical order, so that an element's position depends only on the
    /// document's content and never on the order edits were made in. Two people adding different elements on
    /// two branches then produce changes that merge cleanly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Rules are given per parent element: the order element kinds appear in, and optionally a sort key within a
    /// kind. Kinds a parent's rules do not list come after all listed kinds, in the order they already have.
    /// Keys are compared with the invariant culture, so the order is the same on every machine.
    /// </para>
    /// <para>
    /// Attach an ordering to a document with <see cref="Set"/> (or <c>WixSourceDocument.Ordering</c>): adding an
    /// element through the model then inserts it at its canonical position, and <see cref="Canonicalize(XDocument)"/>
    /// re-sorts what is already there. A parent containing preprocessor conditionals (<c>&lt;?if?&gt;</c>,
    /// <c>&lt;?foreach?&gt;</c>) is never re-sorted, since moving an element across them would change what is
    /// built; elements added to such a parent go at its end.
    /// </para>
    /// </remarks>
    public sealed class WixOrdering
    {
        private static readonly HashSet<string> ConditionalTargets = new HashSet<string>(StringComparer.Ordinal)
        {
            "if", "ifdef", "ifndef", "elseif", "else", "endif", "foreach", "endforeach",
        };

        private readonly Dictionary<XName, Dictionary<XName, int>> _kindOrders = new Dictionary<XName, Dictionary<XName, int>>();
        private readonly Dictionary<XName, Dictionary<XName, Func<XElement, string[]>>> _sortKeys =
            new Dictionary<XName, Dictionary<XName, Func<XElement, string[]>>>();

        /// <summary>Sets the order child element kinds appear in under <paramref name="parent"/>.</summary>
        /// <param name="parent">The parent element name.</param>
        /// <param name="childKinds">Child element names, first to last.</param>
        /// <returns>This ordering, for chaining.</returns>
        public WixOrdering OrderChildren(XName parent, params XName[] childKinds)
        {
            if (parent is null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            var ranks = new Dictionary<XName, int>();
            for (var i = 0; i < (childKinds ?? throw new ArgumentNullException(nameof(childKinds))).Length; i++)
            {
                if (ranks.ContainsKey(childKinds[i]))
                {
                    throw new ArgumentException($"'{childKinds[i].LocalName}' is listed more than once for <{parent.LocalName}>.", nameof(childKinds));
                }

                ranks.Add(childKinds[i], i);
            }

            _kindOrders[parent] = ranks;
            return this;
        }

        /// <summary>Sorts <paramref name="child"/> elements under <paramref name="parent"/> by a key.</summary>
        /// <param name="parent">The parent element name.</param>
        /// <param name="child">The child element name.</param>
        /// <param name="key">
        /// The sort key: one or more strings, compared in turn with the invariant culture. Return empty strings,
        /// not <see langword="null"/>, for missing values.
        /// </param>
        /// <returns>This ordering, for chaining.</returns>
        public WixOrdering SortBy(XName parent, XName child, Func<XElement, string[]> key)
        {
            if (parent is null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (!_sortKeys.TryGetValue(parent, out var keys))
            {
                keys = new Dictionary<XName, Func<XElement, string[]>>();
                _sortKeys.Add(parent, keys);
            }

            keys[child ?? throw new ArgumentNullException(nameof(child))] = key ?? throw new ArgumentNullException(nameof(key));
            return this;
        }

        /// <summary>Gets the ordering attached to a document.</summary>
        /// <param name="document">The document.</param>
        /// <returns>The ordering, or <see langword="null"/> when none is attached.</returns>
        public static WixOrdering? Get(XDocument document) =>
            (document ?? throw new ArgumentNullException(nameof(document))).Annotation<WixOrdering>();

        /// <summary>Attaches an ordering to a document, or removes it with <see langword="null"/>.</summary>
        /// <param name="document">The document.</param>
        /// <param name="ordering">The ordering.</param>
        public static void Set(XDocument document, WixOrdering? ordering)
        {
            if (document is null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            document.RemoveAnnotations<WixOrdering>();
            if (ordering != null)
            {
                document.AddAnnotation(ordering);
            }
        }

        /// <summary>Re-sorts the children of every element in the document. Already-canonical parents are untouched.</summary>
        /// <param name="document">The document.</param>
        /// <returns>The number of parents whose children were re-sorted.</returns>
        public int Canonicalize(XDocument document)
        {
            var root = (document ?? throw new ArgumentNullException(nameof(document))).Root;
            return root is null ? 0 : root.DescendantsAndSelf().ToList().Count(Canonicalize);
        }

        /// <summary>
        /// Re-sorts the children of one element. Each element moves together with the comments directly above it.
        /// Nothing changes when the children are already in canonical order, when the parent has no rules, or when
        /// it holds text or preprocessor conditionals.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns><see langword="true"/> when the children were re-sorted.</returns>
        public bool Canonicalize(XElement parent)
        {
            if (parent is null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (!HasRules(parent.Name) || !CanReorder(parent))
            {
                return false;
            }

            // Group each element with the nodes in front of it since the previous element (its whitespace and comments).
            var groups = new List<List<XNode>>();
            var pending = new List<XNode>();
            foreach (var node in parent.Nodes())
            {
                pending.Add(node);
                if (node is XElement)
                {
                    groups.Add(pending);
                    pending = new List<XNode>();
                }
            }

            var sorted = groups
                .Select((group, index) => (Group: group, Index: index))
                .OrderBy(g => (XElement)g.Group[g.Group.Count - 1], Comparer<XElement>.Create((a, b) => Compare(parent.Name, a, b)))
                .ThenBy(g => g.Index)
                .Select(g => g.Group)
                .ToList();
            if (sorted.SequenceEqual(groups))
            {
                return false;
            }

            parent.RemoveNodes();
            parent.Add(sorted.SelectMany(g => g).Concat(pending).ToArray());
            return true;
        }

        /// <summary>
        /// Finds where a new child goes: after the returned element, or first when it is <see langword="null"/>.
        /// </summary>
        /// <returns><see langword="false"/> when this ordering has no say (no rules for the parent).</returns>
        internal bool TryFindPredecessor(XElement parent, XElement child, out XNode? predecessor)
        {
            predecessor = null;
            if (!HasRules(parent.Name))
            {
                return false;
            }

            if (!CanReorder(parent))
            {
                // Never insert into a conditional block: go after everything.
                predecessor = parent.Nodes().LastOrDefault(n => !IsWhitespace(n));
                return true;
            }

            // Next to its own kind when there is one, so a file that is not (yet) in canonical order still gets the
            // new element beside its siblings: after the last one that sorts at or before it, else in front of the
            // first. In a canonical file this is the same place the kind order gives.
            var sameKind = parent.Elements(child.Name).ToList();
            if (sameKind.Count > 0)
            {
                var after = sameKind.LastOrDefault(s => Compare(parent.Name, s, child) <= 0);
                predecessor = after ?? sameKind[0].ElementsBeforeSelf().LastOrDefault();
                return true;
            }

            foreach (var sibling in parent.Elements())
            {
                if (Compare(parent.Name, sibling, child) <= 0)
                {
                    predecessor = sibling;
                }
            }

            return true;
        }

        /// <summary>Gets the kind order for a parent, first to last (empty when it has none).</summary>
        internal IReadOnlyList<XName> ChildKinds(XName parent) =>
            _kindOrders.TryGetValue(parent, out var ranks)
                ? ranks.OrderBy(r => r.Value).Select(r => r.Key).ToList()
                : (IReadOnlyList<XName>)Array.Empty<XName>();

        private bool HasRules(XName parent) => _kindOrders.ContainsKey(parent) || _sortKeys.ContainsKey(parent);

        private int Compare(XName parent, XElement a, XElement b)
        {
            var rankA = Rank(parent, a.Name);
            var rankB = Rank(parent, b.Name);
            if (rankA != rankB)
            {
                return rankA.CompareTo(rankB);
            }

            if (a.Name != b.Name ||
                !_sortKeys.TryGetValue(parent, out var keys) ||
                !keys.TryGetValue(a.Name, out var key))
            {
                return 0;
            }

            var keyA = key(a);
            var keyB = key(b);
            for (var i = 0; i < Math.Min(keyA.Length, keyB.Length); i++)
            {
                // Invariant culture, not ordinal: it matches the culture-aware order existing IsWiX files were sorted
                // in (ordinal would put every uppercase letter before lowercase), and is the same on every machine.
#pragma warning disable CA1309
                var result = string.Compare(keyA[i] ?? string.Empty, keyB[i] ?? string.Empty, CultureInfo.InvariantCulture, CompareOptions.None);
#pragma warning restore CA1309
                if (result != 0)
                {
                    return result;
                }
            }

            return keyA.Length.CompareTo(keyB.Length);
        }

        private int Rank(XName parent, XName child) =>
            _kindOrders.TryGetValue(parent, out var ranks) && ranks.TryGetValue(child, out var rank) ? rank : int.MaxValue;

        private static bool CanReorder(XElement parent) =>
            !parent.Nodes().Any(n =>
                (n is XText && !IsWhitespace(n)) ||
                (n is XProcessingInstruction pi && ConditionalTargets.Contains(pi.Target))) &&
            !parent.AncestorsAndSelf().Any(e => (string?)e.Attribute(XNamespace.Xml + "space") == "preserve");

        private static bool IsWhitespace(XNode node) =>
            node is XText text && !(node is XCData) && text.Value.Trim(' ', '\t', '\r', '\n').Length == 0;
    }
}
