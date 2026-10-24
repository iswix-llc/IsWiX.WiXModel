using System;
using System.Collections.Generic;
using System.Linq;
using IsWiX.WiXModel.SchemaGen.Schema;

namespace IsWiX.WiXModel.SchemaGen.CodeGen;

internal sealed record GeneratedFile(string FileName, string Content);

/// <summary>Emits the wrapper classes, enums, enum converters and the factory registration.</summary>
internal sealed class ModelEmitter
{
    // Members of WixElement and object that generated members must not hide.
    private static readonly HashSet<string> ReservedMembers = new(StringComparer.Ordinal)
    {
        "Xml", "Parent", "Children", "Wrap", "RemoveFromParent", "Add", "ElementName",
        "GetAttributeValue", "SetAttributeValue", "GetString", "SetString", "GetValue", "SetValue",
        "GetChildren", "AddChild", "ToString", "Equals", "GetHashCode", "GetType",
    };

    private const string Wxs = "http://wixtoolset.org/schemas/v4/wxs";

    private readonly SchemaSet _schema;
    private readonly GeneratorConfig _config;
    private readonly SchemaSet? _baseSchema;
    private readonly GeneratorConfig? _baseConfig;
    private readonly TypeMapper _types;

    /// <param name="schema">The schema to generate.</param>
    /// <param name="config">Its config.</param>
    /// <param name="baseSchema">For an extension, the core WiX schema its types and parents come from.</param>
    /// <param name="baseConfig">For an extension, the core config (which elements have classes, and their namespace).</param>
    public ModelEmitter(SchemaSet schema, GeneratorConfig config, SchemaSet? baseSchema = null, GeneratorConfig? baseConfig = null)
    {
        _schema = schema;
        _config = config;
        _baseSchema = baseSchema;
        _baseConfig = baseConfig;
        _types = new TypeMapper(
            schema,
            config.Namespace,
            baseSchema is null || baseConfig is null ? null : new TypeMapper(baseSchema, baseConfig.Namespace));
    }

    private string Namespace => _config.Namespace;

    public IReadOnlyList<GeneratedFile> Emit()
    {
        // The core schema is in the wxs namespace; an extension is in its own and names the core config as its base.
        if ((_baseConfig is null) != (_schema.TargetNamespace == Wxs))
        {
            throw new InvalidOperationException(_baseConfig is null
                ? $"The schema's target namespace is '{_schema.TargetNamespace}'; an extension schema needs a \"base\" config naming the core one."
                : $"The schema's target namespace is the core '{Wxs}', but the config names a base.");
        }

        var missing = _config.Elements.Where(e => !_schema.Elements.ContainsKey(e)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"Elements not found in the schema: {string.Join(", ", missing)}.");
        }

        var missingAttributes = (_config.Attributes ?? []).Where(a => _schema.Attributes?.ContainsKey(a) != true).ToList();
        if (missingAttributes.Count > 0)
        {
            throw new InvalidOperationException($"Attributes not found in the schema: {string.Join(", ", missingAttributes)}.");
        }

        var elements = _config.Elements.Distinct(StringComparer.Ordinal).Select(e => _schema.Elements[e]).ToList();
        var files = elements.Select(e => new GeneratedFile(e.Name + ".g.cs", EmitElement(e))).ToList();

        // Before the enums: extension attributes on core parents may add some.
        files.AddRange(EmitParentAccessors(elements));
        files.Add(new GeneratedFile("Enums.g.cs", EmitEnums()));
        files.Add(new GeneratedFile("WixEnumConverters.g.cs", EmitEnumConverters()));
        files.Add(new GeneratedFile("WixModelRegistration.g.cs", EmitRegistration(elements)));
        return files.OrderBy(f => f.FileName, StringComparer.Ordinal).ToList();
    }

    private string EmitElement(ElementDefinition element)
    {
        if (element.HasTextContent)
        {
            throw new NotSupportedException($"<{element.Name}> has text content, which the generator does not support yet.");
        }

        var className = element.Name;
        var used = new HashSet<string>(ReservedMembers, StringComparer.Ordinal) { className };
        var code = Header();
        code.Open($"namespace {Namespace}");

        var remarks = new List<string>();
        var otherChildren = element.ChildElements.Where(c => !_config.Elements.Contains(c)).ToList();
        if (otherChildren.Count > 0)
        {
            remarks.Add($"Child elements not yet in the typed model are available through Children and Xml: {string.Join(", ", otherChildren)}.");
        }

        code.Documentation(Naming.Paragraphs(element.Documentation), remarks);
        code.Open($"public sealed partial class {className} : global::IsWiX.WiXModel.Core.WixElement");

        code.Line($"/// <summary>The XML name of the <c>{element.Name}</c> element.</summary>");
        code.Line($"public static readonly global::System.Xml.Linq.XName ElementName = {XmlNamespace} + {Naming.Literal(element.Name)};");
        code.Line();
        code.Line($"/// <summary>Creates a detached <c>{element.Name}</c> element. Add it to a parent to place it in a document.</summary>");
        code.Line($"public {className}()");
        code.Line("    : base(new global::System.Xml.Linq.XElement(ElementName))");
        code.Line("{");
        code.Line("}");
        code.Line();
        code.Line($"internal {className}(global::System.Xml.Linq.XElement xml)");
        code.Line("    : base(xml)");
        code.Line("{");
        code.Line("}");

        foreach (var attribute in element.Attributes)
        {
            code.Line();
            EmitAttribute(code, element, attribute, used);
        }

        var typedChildren = element.ChildElements.Where(c => _config.Elements.Contains(c)).ToList();
        foreach (var child in typedChildren)
        {
            var childRef = $"global::{Namespace}.{child}";
            // Standard actions (InstallFiles, CostFinalize, ...) are verbs that share one content model;
            // "CostFinalizes" is not a word, so they are all named CostFinalizeElements and so on.
            var collectionName = _schema.Elements[child].SharedContentType != null ? child + "Elements" : Naming.Plural(child);
            var propertyName = Naming.Unique(collectionName, used, "Elements");

            code.Line();
            code.Line($"/// <summary>Gets the child <c>{child}</c> elements.</summary>");
            code.Line($"public global::System.Collections.Generic.IEnumerable<{childRef}> {propertyName} => GetChildren<{childRef}>({childRef}.ElementName);");
            code.Line();
            code.Line($"/// <summary>Adds a <c>{child}</c> element after the last existing one, indented to match.</summary>");
            code.Line("/// <param name=\"child\">A detached element.</param>");
            code.Line("/// <returns><paramref name=\"child\"/>, for chaining.</returns>");
            code.Line($"public {childRef} Add({childRef} child) => AddChild(child);");
        }

        code.Close();
        code.Close();
        return code.ToString();
    }

    private void EmitAttribute(CodeWriter code, ElementDefinition element, AttributeDefinition attribute, HashSet<string> used)
    {
        var propertyName = Naming.Unique(attribute.Name, used, "Attribute");
        EmitProperty(code, attribute, propertyName, _types.Map(element, attribute), Naming.Literal(attribute.Name), null);
    }

    /// <param name="name">A C# expression for the attribute's XName.</param>
    /// <param name="summary">The summary, or <see langword="null"/> for the attribute's own documentation.</param>
    private static void EmitProperty(CodeWriter code, AttributeDefinition attribute, string propertyName, AttributeType type, string name, string? summary)
    {
        var remarks = new List<string>();
        if (attribute.Required)
        {
            remarks.Add("This attribute is required.");
        }

        if (attribute.IntroducedInVersion is { } version)
        {
            remarks.Add($"Requires WiX v{version} or later.");
        }

        code.Documentation(summary is null ? Naming.Paragraphs(attribute.Documentation) : [summary, .. Naming.Paragraphs(attribute.Documentation)], remarks);
        if (type.IsString)
        {
            code.Line($"public string? {propertyName}");
            code.Line("{");
            code.Line($"    get => GetString({name});");
            code.Line($"    set => SetString({name}, value);");
            code.Line("}");
        }
        else
        {
            code.Line($"public global::IsWiX.WiXModel.Core.WixValue<{type.LiteralType}>? {propertyName}");
            code.Line("{");
            code.Line($"    get => GetValue({name}, {type.Converter});");
            code.Line($"    set => SetValue({name}, {type.Converter}, value);");
            code.Line("}");
        }
    }

    private string EmitEnums()
    {
        var code = Header();
        code.Open($"namespace {Namespace}");
        var first = true;
        foreach (var model in _types.Enums)
        {
            if (!first)
            {
                code.Line();
            }

            first = false;
            code.Documentation(Naming.Paragraphs(model.Documentation));
            code.Open($"public enum {model.Name}");
            for (var i = 0; i < model.Members.Count; i++)
            {
                var member = model.Members[i];
                var spellings = string.Join(" or ", new[] { member.Value }.Concat(member.Aliases).Select(v => $"<c>{v}</c>"));
                var summary = new List<string> { $"Written as {spellings}." };
                summary.AddRange(Naming.Paragraphs(member.Documentation));
                if (i > 0)
                {
                    code.Line();
                }

                code.Documentation(summary);
                code.Line(member.Name + ",");
            }

            code.Close();
        }

        code.Close();
        return code.ToString();
    }

    private string EmitEnumConverters()
    {
        var code = Header();
        code.Open($"namespace {Namespace}");
        code.Line("/// <summary>");
        code.Line("/// Converters between the generated enums and their attribute text, for editors that list or check values.");
        code.Line("/// </summary>");
        code.Open("public static class WixEnumConverters");
        var first = true;
        foreach (var model in _types.Enums)
        {
            var enumRef = $"global::{Namespace}.{model.Name}";
            if (!first)
            {
                code.Line();
            }

            first = false;
            code.Line($"/// <summary>Reads and writes <see cref=\"{enumRef}\"/> values as the schema spells them.</summary>");
            code.Line($"public static global::IsWiX.WiXModel.Core.WixValueConverter<{enumRef}> {model.Name} {{ get; }} = new {model.Name}Converter();");
        }

        foreach (var model in _types.Enums)
        {
            var enumRef = $"global::{Namespace}.{model.Name}";
            code.Line();
            code.Open($"private sealed class {model.Name}Converter : global::IsWiX.WiXModel.Core.WixValueConverter<{enumRef}>");
            code.Open($"public override bool TryParse(string text, out {enumRef} value)");
            code.Open("switch (text)");
            foreach (var member in model.Members)
            {
                foreach (var spelling in new[] { member.Value }.Concat(member.Aliases))
                {
                    code.Line($"case {Naming.Literal(spelling)}:");
                }

                code.Line($"    value = {enumRef}.{member.Name};");
                code.Line("    return true;");
            }

            code.Line("default:");
            code.Line("    value = default;");
            code.Line("    return false;");
            code.Close();
            code.Close();
            code.Line();
            code.Open($"public override string Format({enumRef} value)");
            code.Open("switch (value)");
            foreach (var member in model.Members)
            {
                code.Line($"case {enumRef}.{member.Name}:");
                code.Line($"    return {Naming.Literal(member.Value)};");
            }

            code.Line("default:");
            code.Line($"    throw new global::System.ArgumentOutOfRangeException(nameof(value), value, \"Not a {model.Name} value.\");");
            code.Close();
            code.Close();
            code.Close();
        }

        code.Close();
        code.Close();
        return code.ToString();
    }

    private string EmitRegistration(IReadOnlyList<ElementDefinition> elements)
    {
        var code = Header();
        code.Open($"namespace {Namespace}");
        code.Open("internal static class WixModelRegistration");
        code.Line("#pragma warning disable CA2255 // Registering wrappers before any model type is used is what module initializers are for.");
        code.Line("[global::System.Runtime.CompilerServices.ModuleInitializer]");
        code.Line("#pragma warning restore CA2255");
        code.Open("internal static void Register()");
        foreach (var element in elements)
        {
            var classRef = $"global::{Namespace}.{element.Name}";
            code.Line($"global::IsWiX.WiXModel.Core.WixElementFactory.Register({classRef}.ElementName, xml => new {classRef}(xml));");
        }

        code.Close();
        code.Close();
        code.Close();
        return code.ToString();
    }

    // The XNamespace expression generated ElementName fields use.
    private string XmlNamespace => _schema.TargetNamespace == Wxs
        ? "global::IsWiX.WiXModel.Core.WixNamespaces.Wxs"
        : $"global::System.Xml.Linq.XNamespace.Get({Naming.Literal(_schema.TargetNamespace)})";

    /// <summary>
    /// For an extension: the core elements its elements may be nested in get a typed collection and Add overload
    /// for each, and the core elements its top-level attributes go on get a typed property for each, in a partial
    /// class next to the generated core one (<c>component.UtilUsers</c>, <c>component.Add(new Util.User())</c>,
    /// <c>variable.BalOverridable</c>).
    /// </summary>
    private IEnumerable<GeneratedFile> EmitParentAccessors(IReadOnlyList<ElementDefinition> elements)
    {
        if (_baseConfig is null || _baseSchema is null)
        {
            yield break;
        }

        var prefix = _config.MemberPrefix ?? throw new InvalidOperationException("An extension config needs a \"memberPrefix\" (possibly empty) for the members it adds to core classes.");

        // WiX's usual prefix for the namespace: its last segment (util, netfx, ui).
        var xmlPrefix = _schema.TargetNamespace[(_schema.TargetNamespace.LastIndexOf('/') + 1)..];
        bool IsCoreParent(ParentReference p) => p.Namespace == _baseSchema.TargetNamespace && _baseConfig.Elements.Contains(p.Name);
        var children = elements
            .SelectMany(e => (e.Parents ?? []).Where(IsCoreParent).Select(p => (Parent: p.Name, Child: e.Name)))
            .ToLookup(p => p.Parent, p => p.Child, StringComparer.Ordinal);
        var attributes = (_config.Attributes ?? []).Distinct(StringComparer.Ordinal).Select(a => _schema.Attributes![a])
            .SelectMany(a => a.Parents.Where(IsCoreParent).Select(p => (Parent: p.Name, Attribute: a.Definition)))
            .ToLookup(p => p.Parent, p => p.Attribute, StringComparer.Ordinal);
        var parents = children.Select(g => g.Key).Union(attributes.Select(g => g.Key), StringComparer.Ordinal).Order(StringComparer.Ordinal);
        foreach (var parent in parents)
        {
            var code = Header();
            code.Open($"namespace {_baseConfig.Namespace}");
            code.Open($"public sealed partial class {parent}");
            var first = true;
            foreach (var child in children[parent].Order(StringComparer.Ordinal))
            {
                var childRef = $"global::{Namespace}.{child}";
                if (!first)
                {
                    code.Line();
                }

                first = false;
                code.Line($"/// <summary>Gets the child <c>{xmlPrefix}:{child}</c> elements.</summary>");
                code.Line($"public global::System.Collections.Generic.IEnumerable<{childRef}> {prefix}{Naming.Plural(child)} => GetChildren<{childRef}>({childRef}.ElementName);");
                code.Line();
                code.Line($"/// <summary>Adds a <c>{xmlPrefix}:{child}</c> element, declaring its namespace on the root if needed.</summary>");
                code.Line("/// <param name=\"child\">A detached element.</param>");
                code.Line("/// <returns><paramref name=\"child\"/>, for chaining.</returns>");
                code.Line($"public {childRef} Add({childRef} child) => AddChild(child);");
            }

            foreach (var attribute in attributes[parent].OrderBy(a => a.Name, StringComparer.Ordinal))
            {
                if (!first)
                {
                    code.Line();
                }

                first = false;
                EmitProperty(
                    code,
                    attribute,
                    prefix + attribute.Name,
                    _types.MapGlobal(attribute),
                    $"{XmlNamespace} + {Naming.Literal(attribute.Name)}",
                    $"The <c>{xmlPrefix}:{attribute.Name}</c> attribute. Setting it declares its namespace on the root if needed.");
            }

            code.Close();
            code.Close();
            yield return new GeneratedFile($"{parent}.{_config.Namespace[(_config.Namespace.LastIndexOf('.') + 1)..]}.g.cs", code.ToString());
        }
    }

    private CodeWriter Header() => new CodeWriter()
        .Line("// <auto-generated>")
        .Line($"//     Generated by IsWiX.WiXModel.SchemaGen from the WiX {_config.SchemaVersion} schema.")
        .Line($"//     Do not edit: change {_config.FileName} or the generator and regenerate.")
        .Line("// </auto-generated>")
        .Line()
        .Line("#nullable enable")
        .Line();
}
