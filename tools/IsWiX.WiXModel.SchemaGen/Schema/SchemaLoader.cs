using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace IsWiX.WiXModel.SchemaGen.Schema;

/// <summary>
/// Reads the WiX repository's per-element schema fragments. Each file holds one top-level
/// <c>element</c>, <c>simpleType</c>, <c>complexType</c> or <c>attributeGroup</c> written without a namespace (the WiX build
/// wraps them in an <c>xs:schema</c>), so names are matched by local name.
/// </summary>
internal static class SchemaLoader
{
    public static SchemaSet Load(string directory)
    {
        var elements = new Dictionary<string, XElement>(StringComparer.Ordinal);
        var simpleTypes = new Dictionary<string, SimpleTypeDefinition>(StringComparer.Ordinal);
        var attributeGroups = new Dictionary<string, XElement>(StringComparer.Ordinal);
        var complexTypes = new Dictionary<string, XElement>(StringComparer.Ordinal);
        string? targetNamespace = null;

        foreach (var file in Directory.EnumerateFiles(directory, "*.xsd").OrderBy(f => f, StringComparer.Ordinal))
        {
            var root = XDocument.Load(file).Root!;
            if (root.Name.LocalName == "schema")
            {
                // ns.xsd: the wrapper the WiX build puts the fragments in.
                targetNamespace = (string?)root.Attribute("targetNamespace");
                continue;
            }

            var name = (string?)root.Attribute("name")
                ?? throw new InvalidDataException($"{Path.GetFileName(file)}: the top-level <{root.Name.LocalName}> has no name.");
            switch (root.Name.LocalName)
            {
                case "element":
                    elements.Add(name, root);
                    break;
                case "simpleType":
                    simpleTypes.Add(name, ReadSimpleType(root, name));
                    break;
                case "attributeGroup":
                    attributeGroups.Add(name, root);
                    break;
                case "complexType":
                    // Shared content models such as ActionSequenceType, used by the standard actions.
                    if (Child(root, "complexContent") != null)
                    {
                        throw new NotSupportedException($"complexType '{name}' derives from another type; derivation is not supported.");
                    }

                    complexTypes.Add(name, root);
                    break;
                default:
                    throw new NotSupportedException($"{Path.GetFileName(file)}: top-level <{root.Name.LocalName}> is not supported.");
            }
        }

        var definitions = elements.ToDictionary(
            e => e.Key,
            e => ReadElement(e.Value, e.Key, attributeGroups, complexTypes),
            StringComparer.Ordinal);
        return new SchemaSet(
            targetNamespace ?? throw new InvalidDataException($"{directory} has no ns.xsd declaring the target namespace."),
            definitions,
            simpleTypes);
    }

    private static ElementDefinition ReadElement(
        XElement element,
        string name,
        Dictionary<string, XElement> attributeGroups,
        Dictionary<string, XElement> complexTypes)
    {
        var complexType = Child(element, "complexType");
        string? sharedContentType = null;
        if (complexType is null)
        {
            switch ((string?)element.Attribute("type"))
            {
                case null:
                    // <element name="X"/> with no content model: an empty element with no attributes.
                    return new ElementDefinition(name, Documentation(element), [], [], false, false, false);
                case var typeName when complexTypes.TryGetValue(typeName, out var named):
                    complexType = named;
                    sharedContentType = typeName;
                    break;
                default:
                    // A simple type: the element holds text.
                    return new ElementDefinition(name, Documentation(element), [], [], false, false, true);
            }
        }

        var children = new List<string>();
        var allowsAny = false;
        void ReadParticles(XElement group)
        {
            foreach (var particle in group.Elements())
            {
                switch (particle.Name.LocalName)
                {
                    case "choice" or "sequence" or "all":
                        ReadParticles(particle);
                        break;
                    case "element" when particle.Attribute("ref") is { } reference:
                        if (!children.Contains(reference.Value, StringComparer.Ordinal))
                        {
                            children.Add(reference.Value);
                        }

                        break;
                    case "element":
                        throw new NotSupportedException($"<{name}> declares a local element '{(string?)particle.Attribute("name")}'; only element references are supported.");
                    case "any":
                        allowsAny = true;
                        break;
                }
            }
        }

        ReadParticles(complexType);

        var attributes = new List<AttributeDefinition>();
        var hasText = Child(complexType, "simpleContent") != null || (string?)complexType.Attribute("mixed") == "true";
        var attributeOwner = Child(complexType, "simpleContent") is { } simpleContent
            ? simpleContent.Elements().First()
            : complexType;
        ReadAttributes(attributeOwner, attributes, attributeGroups);

        return new ElementDefinition(
            name,
            Documentation(element),
            attributes,
            children,
            allowsAny,
            attributeOwner.Elements().Any(e => e.Name.LocalName == "anyAttribute"),
            hasText,
            sharedContentType);
    }

    private static void ReadAttributes(XElement owner, List<AttributeDefinition> attributes, Dictionary<string, XElement> attributeGroups)
    {
        foreach (var child in owner.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "attribute":
                    var attributeName = (string?)child.Attribute("name")
                        ?? throw new NotSupportedException($"Attribute references ('{(string?)child.Attribute("ref")}') are not supported.");
                    var inline = Child(child, "simpleType");
                    attributes.Add(new AttributeDefinition(
                        attributeName,
                        (string?)child.Attribute("type"),
                        inline is null ? null : ReadSimpleType(inline, null),
                        (string?)child.Attribute("use") == "required",
                        Documentation(child),
                        (string?)child.Attribute("version")));
                    break;
                case "attributeGroup":
                    var groupName = (string)child.Attribute("ref")!;
                    ReadAttributes(attributeGroups[groupName], attributes, attributeGroups);
                    break;
            }
        }
    }

    private static SimpleTypeDefinition ReadSimpleType(XElement simpleType, string? name)
    {
        var documentation = Documentation(simpleType);
        if (Child(simpleType, "union") is { } union)
        {
            var members = ((string?)union.Attribute("memberTypes") ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return new SimpleTypeDefinition(name, SimpleTypeKind.Union, documentation, null, [], members);
        }

        if (Child(simpleType, "list") != null)
        {
            return new SimpleTypeDefinition(name, SimpleTypeKind.List, documentation, null, [], []);
        }

        var restriction = Child(simpleType, "restriction")
            ?? throw new NotSupportedException($"simpleType '{name}' is neither a restriction nor a union.");
        var values = restriction.Elements()
            .Where(e => e.Name.LocalName == "enumeration")
            .Select(e => new EnumerationValue((string)e.Attribute("value")!, Documentation(e)))
            .ToList();
        return new SimpleTypeDefinition(
            name,
            values.Count > 0 ? SimpleTypeKind.Enumeration : SimpleTypeKind.Restriction,
            documentation,
            (string?)restriction.Attribute("base"),
            values,
            []);
    }

    private static XElement? Child(XElement element, string localName) =>
        element.Elements().FirstOrDefault(e => e.Name.LocalName == localName);

    private static string? Documentation(XElement element)
    {
        var documentation = Child(element, "annotation") is { } annotation ? Child(annotation, "documentation") : null;
        return documentation?.Value;
    }
}
