using System;
using System.Collections.Generic;
using System.Linq;
using IsWiX.WiXModel.SchemaGen.Schema;

namespace IsWiX.WiXModel.SchemaGen.CodeGen;

/// <summary>How an attribute is exposed: a plain string, or a <c>WixValue&lt;T&gt;</c> with a converter.</summary>
internal sealed record AttributeType(string? LiteralType, string? Converter)
{
    public static readonly AttributeType String = new(null, null);
    public static readonly AttributeType Boolean = new("bool", "global::IsWiX.WiXModel.Core.WixConverters.YesNo");
    public static readonly AttributeType Int32 = new("int", "global::IsWiX.WiXModel.Core.WixConverters.Int32");
    public static readonly AttributeType Int64 = new("long", "global::IsWiX.WiXModel.Core.WixConverters.Int64");

    public bool IsString => LiteralType is null;
}

internal sealed record EnumModel(string Name, string? Documentation, IReadOnlyList<EnumMember> Members);

internal sealed record EnumMember(string Name, string Value, IReadOnlyList<string> Aliases, string? Documentation);

/// <summary>Maps schema types to C# types.</summary>
internal sealed class TypeMapper
{
    // Pattern-restricted string types whose literal form is an integer.
    private static readonly HashSet<string> IntegerTypes = new(StringComparer.Ordinal)
    {
        "Integer", "LocalizableInteger", "DiskIdType", "NegativeInteger",
    };

    // Union members that only admit build-time expressions. WixValue<T> already represents those.
    private static readonly HashSet<string> ExpressionTypes = new(StringComparer.Ordinal)
    {
        "PreprocessorVariables",
    };

    private readonly SchemaSet _schema;
    private readonly string _namespace;
    private readonly Dictionary<string, EnumModel> _enums = new(StringComparer.Ordinal);

    public TypeMapper(SchemaSet schema, string @namespace)
    {
        _schema = schema;
        _namespace = @namespace;
    }

    /// <summary>Gets the enums used by the attributes mapped so far.</summary>
    public IEnumerable<EnumModel> Enums => _enums.Values.OrderBy(e => e.Name, StringComparer.Ordinal);

    public AttributeType Map(ElementDefinition owner, AttributeDefinition attribute) =>
        attribute.InlineType is { } inline
            ? MapSimpleType(inline, InlineEnumName(owner, attribute))
            : MapNamedType(attribute.TypeName);

    private AttributeType MapNamedType(string? typeName)
    {
        switch (typeName)
        {
            case null:
                return AttributeType.String;
            case "xs:int" or "xs:integer" or "xs:nonNegativeInteger" or "xs:positiveInteger" or "xs:unsignedInt":
                return AttributeType.Int32;
            case "xs:long" or "xs:unsignedLong":
                return AttributeType.Int64;
            case var builtIn when builtIn.StartsWith("xs:", StringComparison.Ordinal):
                return AttributeType.String;
            case var integer when IntegerTypes.Contains(integer):
                return AttributeType.Int32;
        }

        if (!_schema.SimpleTypes.TryGetValue(typeName, out var definition))
        {
            throw new InvalidOperationException($"Type '{typeName}' is not defined in the schema.");
        }

        return MapSimpleType(definition, typeName);
    }

    private AttributeType MapSimpleType(SimpleTypeDefinition definition, string name)
    {
        switch (definition.Kind)
        {
            case SimpleTypeKind.Union:
                var members = definition.UnionMembers.Where(m => !ExpressionTypes.Contains(m)).ToList();
                return members.Count == 1 ? MapNamedType(members[0]) : AttributeType.String;

            case SimpleTypeKind.Enumeration:
                var values = definition.Values.Select(v => v.Value).ToHashSet(StringComparer.Ordinal);
                if (values.SetEquals(["yes", "no"]) || values.SetEquals(["yes", "no", "true", "false"]))
                {
                    return AttributeType.Boolean;
                }

                var model = GetOrAddEnum(name, definition);
                return new AttributeType(
                    $"global::{_namespace}.{model.Name}",
                    $"global::{_namespace}.WixEnumConverters.{model.Name}");

            default:
                return definition.BaseType is { } baseType && baseType.StartsWith("xs:", StringComparison.Ordinal)
                    ? MapNamedType(baseType)
                    : AttributeType.String;
        }
    }

    private EnumModel GetOrAddEnum(string name, SimpleTypeDefinition definition)
    {
        if (_enums.TryGetValue(name, out var existing))
        {
            return existing;
        }

        var hasYes = definition.Values.Any(v => v.Value == "yes");
        var hasNo = definition.Values.Any(v => v.Value == "no");
        var members = new List<EnumMember>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in definition.Values)
        {
            // "true"/"false" are spellings of "yes"/"no" (YesNoDefaultType lists all four).
            if ((value.Value == "true" && hasYes) || (value.Value == "false" && hasNo))
            {
                continue;
            }

            var aliases = value.Value switch
            {
                "yes" when definition.Values.Any(v => v.Value == "true") => new[] { "true" },
                "no" when definition.Values.Any(v => v.Value == "false") => new[] { "false" },
                _ => Array.Empty<string>(),
            };

            var memberName = Naming.Unique(Naming.Pascal(value.Value), used);
            members.Add(new EnumMember(memberName, value.Value, aliases, value.Documentation));
        }

        var model = new EnumModel(name, definition.Documentation, members);
        _enums.Add(name, model);
        return model;
    }

    private string InlineEnumName(ElementDefinition owner, AttributeDefinition attribute)
    {
        var name = owner.Name + attribute.Name;
        return _schema.Elements.ContainsKey(name) || _schema.SimpleTypes.ContainsKey(name) ? name + "Type" : name;
    }
}
