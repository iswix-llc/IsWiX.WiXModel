using System.Collections.Generic;

namespace IsWiX.WiXModel.SchemaGen.Schema;

internal sealed record SchemaSet(
    string TargetNamespace,
    IReadOnlyDictionary<string, ElementDefinition> Elements,
    IReadOnlyDictionary<string, SimpleTypeDefinition> SimpleTypes,
    IReadOnlyDictionary<string, GlobalAttribute>? Attributes = null);

/// <summary>A top-level attribute an extension schema adds to elements of another namespace (bal:Overridable).</summary>
internal sealed record GlobalAttribute(AttributeDefinition Definition, IReadOnlyList<ParentReference> Parents);

internal sealed record ElementDefinition(
    string Name,
    string? Documentation,
    IReadOnlyList<AttributeDefinition> Attributes,
    IReadOnlyList<string> ChildElements,
    bool AllowsExtensionElements,
    bool AllowsExtensionAttributes,
    bool HasTextContent,
    string? SharedContentType = null,
    IReadOnlyList<ParentReference>? Parents = null);

/// <summary>An element of another namespace this one may be nested in (an extension schema's &lt;parent&gt;).</summary>
internal sealed record ParentReference(string Namespace, string Name);

internal sealed record AttributeDefinition(
    string Name,
    string? TypeName,
    SimpleTypeDefinition? InlineType,
    bool Required,
    string? Documentation,
    string? IntroducedInVersion);

internal enum SimpleTypeKind
{
    /// <summary>A restriction with enumeration values.</summary>
    Enumeration,

    /// <summary>A union of other simple types.</summary>
    Union,

    /// <summary>A restriction of a built-in type, usually by pattern.</summary>
    Restriction,

    /// <summary>A whitespace-separated list (exposed as a string for now).</summary>
    List,
}

internal sealed record SimpleTypeDefinition(
    string? Name,
    SimpleTypeKind Kind,
    string? Documentation,
    string? BaseType,
    IReadOnlyList<EnumerationValue> Values,
    IReadOnlyList<string> UnionMembers);

internal sealed record EnumerationValue(string Value, string? Documentation);
