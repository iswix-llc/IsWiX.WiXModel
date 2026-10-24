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

    private readonly SchemaSet _schema;
    private readonly GeneratorConfig _config;
    private readonly TypeMapper _types;

    public ModelEmitter(SchemaSet schema, GeneratorConfig config)
    {
        _schema = schema;
        _config = config;
        _types = new TypeMapper(schema, config.Namespace);
    }

    private string Namespace => _config.Namespace;

    public IReadOnlyList<GeneratedFile> Emit()
    {
        // Generated ElementName fields use IsWiX.WiXModel.Core.WixNamespaces.Wxs.
        const string Wxs = "http://wixtoolset.org/schemas/v4/wxs";
        if (_schema.TargetNamespace != Wxs)
        {
            throw new InvalidOperationException($"The schema's target namespace is '{_schema.TargetNamespace}', but the generator emits names in '{Wxs}'.");
        }

        var missing = _config.Elements.Where(e => !_schema.Elements.ContainsKey(e)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"Elements not found in the schema: {string.Join(", ", missing)}.");
        }

        var elements = _config.Elements.Distinct(StringComparer.Ordinal).Select(e => _schema.Elements[e]).ToList();
        var files = elements.Select(e => new GeneratedFile(e.Name + ".g.cs", EmitElement(e))).ToList();
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
        code.Line($"public static readonly global::System.Xml.Linq.XName ElementName = global::IsWiX.WiXModel.Core.WixNamespaces.Wxs + {Naming.Literal(element.Name)};");
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
        var type = _types.Map(element, attribute);
        var remarks = new List<string>();
        if (attribute.Required)
        {
            remarks.Add("This attribute is required.");
        }

        if (attribute.IntroducedInVersion is { } version)
        {
            remarks.Add($"Requires WiX v{version} or later.");
        }

        code.Documentation(Naming.Paragraphs(attribute.Documentation), remarks);
        var name = Naming.Literal(attribute.Name);
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
        code.Open("internal static class WixEnumConverters");
        foreach (var model in _types.Enums)
        {
            var enumRef = $"global::{Namespace}.{model.Name}";
            code.Line($"public static readonly global::IsWiX.WiXModel.Core.WixValueConverter<{enumRef}> {model.Name} = new {model.Name}Converter();");
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

    private CodeWriter Header() => new CodeWriter()
        .Line("// <auto-generated>")
        .Line($"//     Generated by IsWiX.WiXModel.SchemaGen from the WiX {_config.SchemaVersion} schema.")
        .Line("//     Do not edit: change src/IsWiX.WiXModel/schemagen.json or the generator and regenerate.")
        .Line("// </auto-generated>")
        .Line()
        .Line("#nullable enable")
        .Line();
}
