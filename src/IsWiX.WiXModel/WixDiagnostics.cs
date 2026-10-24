using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using IsWiX.WiXModel.Core;

namespace IsWiX.WiXModel
{
    /// <summary>How serious a <see cref="WixDiagnostic"/> is.</summary>
    public enum WixDiagnosticSeverity
    {
        /// <summary>Probably a mistake, but the build may still succeed (for example, a reference a library resolves).</summary>
        Warning,

        /// <summary>The WiX build will fail.</summary>
        Error,
    }

    /// <summary>A problem found by <see cref="WixDiagnostics.Validate(IEnumerable{WixSourceDocument}, bool)"/>.</summary>
    public sealed class WixDiagnostic
    {
        internal WixDiagnostic(string code, WixDiagnosticSeverity severity, string message, WixSourceDocument document, XElement element, XName? attribute)
        {
            Code = code;
            Severity = severity;
            Message = message;
            Document = document;
            Element = element;
            Attribute = attribute;
            var location = (IXmlLineInfo?)(attribute is null ? null : element.Attribute(attribute)) ?? element;
            if (location.HasLineInfo())
            {
                Line = location.LineNumber;
                Column = location.LinePosition;
            }
        }

        /// <summary>
        /// Gets the diagnostic's code: <c>WXM001</c> duplicate Id, <c>WXM002</c> invalid attribute value,
        /// <c>WXM003</c> reference to something the files don't define.
        /// </summary>
        public string Code { get; }

        /// <summary>Gets how serious the problem is.</summary>
        public WixDiagnosticSeverity Severity { get; }

        /// <summary>Gets a description of the problem.</summary>
        public string Message { get; }

        /// <summary>Gets the document the problem is in.</summary>
        public WixSourceDocument Document { get; }

        /// <summary>Gets the element the problem is on.</summary>
        public XElement Element { get; }

        /// <summary>Gets the attribute the problem is on, if it is about one.</summary>
        public XName? Attribute { get; }

        /// <summary>Gets the 1-based line in the loaded file, or <see langword="null"/> for content added in code.</summary>
        public int? Line { get; }

        /// <summary>Gets the 1-based column in the loaded file, or <see langword="null"/>.</summary>
        public int? Column { get; }

        /// <inheritdoc/>
        public override string ToString() =>
            $"{(Line is null ? string.Empty : $"({Line},{Column}): ")}{(Severity == WixDiagnosticSeverity.Error ? "error" : "warning")} {Code}: {Message}";
    }

    /// <summary>
    /// Finds common mistakes in WiX source files without building them: duplicate Ids, attribute values WiX won't
    /// accept, and references to things the files don't define. Reading never changes the documents.
    /// </summary>
    public static class WixDiagnostics
    {
        /// <summary>Duplicate Id.</summary>
        public const string DuplicateId = "WXM001";

        /// <summary>Invalid attribute value.</summary>
        public const string InvalidValue = "WXM002";

        /// <summary>Unresolved reference.</summary>
        public const string UnresolvedReference = "WXM003";

        // Element kinds whose Ids share one table across the whole build, so two with the same Id collide. Left
        // out: StandardDirectory (each fragment may open the same one) and kinds whose Ids are scoped to a parent
        // (Column, Control, Verb).
        private static readonly Dictionary<string, string> SymbolTables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Binary"] = "Binary",
            ["Component"] = "Component",
            ["ComponentGroup"] = "ComponentGroup",
            ["CustomAction"] = "CustomAction",
            ["CustomTable"] = "CustomTable",
            ["Dialog"] = "Dialog",
            ["Directory"] = "Directory",
            ["Environment"] = "Environment",
            ["Feature"] = "Feature",
            ["FeatureGroup"] = "FeatureGroup",
            ["File"] = "File",
            ["Icon"] = "Icon",
            ["IniFile"] = "IniFile",
            ["Merge"] = "Merge",
            ["Property"] = "Property",
            ["RegistryValue"] = "Registry",
            ["RemoveFile"] = "RemoveFile",
            ["ServiceControl"] = "ServiceControl",
            ["ServiceInstall"] = "ServiceInstall",
            ["Shortcut"] = "Shortcut",
        };

        // References checked across files, and the element each one must find.
        private static readonly Dictionary<string, string> References = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ComponentGroupRef"] = "ComponentGroup",
            ["ComponentRef"] = "Component",
            ["FeatureGroupRef"] = "FeatureGroup",
            ["FeatureRef"] = "Feature",
            ["MergeRef"] = "Merge",
        };

        private static readonly HashSet<string> Opens = new HashSet<string>(StringComparer.Ordinal) { "if", "ifdef", "ifndef", "foreach" };
        private static readonly HashSet<string> Closes = new HashSet<string>(StringComparer.Ordinal) { "endif", "endforeach" };
        private static readonly ConcurrentDictionary<Type, IReadOnlyList<PropertyInfo>> TypedProperties = new ConcurrentDictionary<Type, IReadOnlyList<PropertyInfo>>();

        /// <summary>
        /// Checks the files of one build together: duplicate Ids within and across them, invalid attribute values,
        /// and (when <paramref name="checkReferences"/>) references that none of them define.
        /// </summary>
        /// <param name="documents">The source and include files.</param>
        /// <param name="checkReferences">
        /// Whether to report unresolved references. They are warnings, since a .wixlib or extension may define them.
        /// </param>
        /// <returns>The problems found, in document order.</returns>
        public static IReadOnlyList<WixDiagnostic> Validate(IEnumerable<WixSourceDocument> documents, bool checkReferences = true)
        {
            var list = (documents ?? throw new ArgumentNullException(nameof(documents))).ToList();
            var diagnostics = new List<WixDiagnostic>();
            var elements = list.SelectMany(d => Elements(d).Select(e => (Document: d, e.Element, e.Conditional))).ToList();

            // Duplicate Ids. Elements inside <?if?> or <?foreach?> blocks are skipped: alternate branches often
            // define the same Id on purpose. So are virtual and override Ids, which redefine a symbol on purpose.
            var firsts = new Dictionary<(string Table, string Id), (WixSourceDocument Document, XElement Element)>();
            foreach (var (document, element, conditional) in elements)
            {
                if (conditional || !SymbolTables.TryGetValue(element.Name.LocalName, out var table) || !IsLiteralId(element, out var id) ||
                    !TryGetSymbolName(id, out var name))
                {
                    continue;
                }

                id = name;

                if (firsts.TryGetValue((table, id), out var first))
                {
                    diagnostics.Add(new WixDiagnostic(DuplicateId, WixDiagnosticSeverity.Error,
                        $"<{element.Name.LocalName} Id=\"{id}\"> is defined more than once; the first is {Where(first.Document, first.Element, document)}.",
                        document, element, "Id"));
                }
                else
                {
                    firsts.Add((table, id), (document, element));
                }
            }

            // Attribute values WiX won't accept: neither a valid value nor a $(var)/!(loc) expression.
            foreach (var (document, element, _) in elements)
            {
                diagnostics.AddRange(InvalidValues(document, element));
            }

            if (checkReferences)
            {
                var defined = new HashSet<(string Kind, string Id)>(elements
                    .Where(e => IsLiteralId(e.Element, out _))
                    .Select(e => (e.Element.Name.LocalName, (string)e.Element.Attribute("Id")!)));
                foreach (var (document, element, _) in elements)
                {
                    if (References.TryGetValue(element.Name.LocalName, out var kind) && IsLiteralId(element, out var id) && !defined.Contains((kind, id)))
                    {
                        diagnostics.Add(new WixDiagnostic(UnresolvedReference, WixDiagnosticSeverity.Warning,
                            $"<{element.Name.LocalName} Id=\"{id}\"> refers to a <{kind}> these files don't define (it may come from a .wixlib or extension).",
                            document, element, "Id"));
                    }
                }
            }

            return diagnostics
                .OrderBy(d => list.IndexOf(d.Document))
                .ThenBy(d => d.Line ?? int.MaxValue)
                .ThenBy(d => d.Column ?? int.MaxValue)
                .ToList();
        }

        /// <summary>Checks files of one build together, including references. See <see cref="Validate(IEnumerable{WixSourceDocument}, bool)"/>.</summary>
        /// <param name="documents">The source and include files.</param>
        /// <returns>The problems found.</returns>
        public static IReadOnlyList<WixDiagnostic> Validate(params WixSourceDocument[] documents) => Validate((IEnumerable<WixSourceDocument>)documents);

        // Every WiX element in document order, and whether it is inside a conditional block.
        private static IEnumerable<(XElement Element, bool Conditional)> Elements(WixSourceDocument document)
        {
            var depth = 0;
            foreach (var node in document.Xml.DescendantNodes())
            {
                if (node is XProcessingInstruction instruction)
                {
                    if (Opens.Contains(instruction.Target))
                    {
                        depth++;
                    }
                    else if (Closes.Contains(instruction.Target))
                    {
                        depth = Math.Max(0, depth - 1);
                    }
                }
                else if (node is XElement element && element.Name.Namespace == WixNamespaces.Wxs)
                {
                    yield return (element, depth > 0);
                }
            }
        }

        // WiX v4+ Ids may start with an access modifier ("public X", "private X"), which is not part of the name.
        // "virtual X" and "override X" redefine a symbol on purpose, so they are not compared.
        private static bool TryGetSymbolName(string id, out string name)
        {
            name = id;
            var space = id.IndexOf(' ');
            if (space < 0)
            {
                return true;
            }

            var modifier = id.Substring(0, space);
            name = id.Substring(space + 1).Trim();
            return modifier == "public" || modifier == "internal" || modifier == "protected" || modifier == "private";
        }

        private static bool IsLiteralId(XElement element, out string id)
        {
            id = (string?)element.Attribute("Id") ?? string.Empty;
            return id.Length > 0 && !WixExpression.ContainsExpression(id);
        }

        private static string Where(WixSourceDocument document, XElement element, WixSourceDocument from)
        {
            var info = (IXmlLineInfo)element;
            var line = info.HasLineInfo() ? $"line {info.LineNumber}" : "added in code";
            return document == from ? line : $"{line} of another file";
        }

        private static IEnumerable<WixDiagnostic> InvalidValues(WixSourceDocument document, XElement element)
        {
            var wrapper = WixElement.Wrap(element);
            foreach (var property in TypedProperties.GetOrAdd(wrapper.GetType(), FindTypedProperties))
            {
                // Generated properties are named after their attribute.
                if (element.Attribute(property.Name) is null)
                {
                    continue;
                }

                var value = property.GetValue(wrapper);
                if (value is null)
                {
                    continue;
                }

                var valueType = value.GetType();
                if (!Equals(valueType.GetProperty(nameof(WixValue<int>.Kind))!.GetValue(value), WixValueKind.Invalid))
                {
                    continue;
                }

                var text = (string?)valueType.GetProperty(nameof(WixValue<int>.Text))!.GetValue(value);
                yield return new WixDiagnostic(InvalidValue, WixDiagnosticSeverity.Error,
                    $"'{text}' is not a valid {element.Name.LocalName}/@{property.Name}; expected {Expected(valueType.GetGenericArguments()[0])}.",
                    document, element, property.Name);
            }
        }

        private static IReadOnlyList<PropertyInfo> FindTypedProperties(Type type) =>
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0 &&
                    Nullable.GetUnderlyingType(p.PropertyType) is Type underlying &&
                    underlying.IsGenericType && underlying.GetGenericTypeDefinition() == typeof(WixValue<>))
                .ToList();

        private static string Expected(Type literal)
        {
            if (literal == typeof(bool))
            {
                return "yes or no";
            }

            if (literal.IsEnum)
            {
                var converter = typeof(WixEnumConverters).GetProperty(literal.Name, BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var format = converter?.GetType().GetMethod("Format", new[] { literal });
                if (format != null)
                {
                    var choices = Enum.GetValues(literal).Cast<object>().Select(v => (string)format.Invoke(converter, new[] { v })!).ToList();
                    return string.Join(", ", choices.Take(choices.Count - 1)) + (choices.Count > 1 ? " or " : string.Empty) + choices.Last();
                }
            }

            return literal == typeof(int) || literal == typeof(long) ? "a number" : "a valid value";
        }
    }
}
