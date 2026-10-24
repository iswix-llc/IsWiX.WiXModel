using System.IO;
using System.Linq;
using IsWiX.WiXModel.SchemaGen;
using IsWiX.WiXModel.SchemaGen.Schema;
using Xunit;

namespace IsWiX.WiXModel.Tests;

/// <summary>Reading the vendored WiX schema, independent of which elements the model generates.</summary>
public class SchemaLoaderTests
{
    private static readonly SchemaSet Schema = SchemaLoader.Load(Path.Combine(GeneratorTests.RepositoryRoot(), "schemas", "wix"));

    [Theory]
    [InlineData("InstallFiles")]
    [InlineData("CostFinalize")]
    [InlineData("WriteRegistryValues")]
    public void Standard_actions_get_attributes_from_ActionSequenceType(string name)
    {
        var element = Schema.Elements[name];

        Assert.Equal(["Condition", "Sequence", "Suppress"], element.Attributes.Select(a => a.Name));
        Assert.False(element.HasTextContent);
        Assert.Empty(element.ChildElements);
    }

    [Theory]
    [InlineData("RemoveExistingProducts")]
    [InlineData("LaunchConditions")]
    [InlineData("FindRelatedProducts")]
    public void Module_sequenceable_actions_get_attributes_from_ActionModuleSequenceType(string name)
    {
        var element = Schema.Elements[name];

        Assert.Equal(["After", "Before", "Condition", "Overridable", "Sequence", "Suppress"], element.Attributes.Select(a => a.Name));
        Assert.False(element.HasTextContent);
    }

    [Fact]
    public void Element_without_a_content_model_is_empty_not_text()
    {
        var element = Schema.Elements["All"];

        Assert.False(element.HasTextContent);
        Assert.Empty(element.Attributes);
        Assert.Empty(element.ChildElements);
    }

    [Fact]
    public void Every_element_in_the_schema_can_be_generated()
    {
        var config = GeneratorConfig.Load(Path.Combine(GeneratorTests.RepositoryRoot(), "src", "IsWiX.WiXModel", "schemagen.json"))
            with { Elements = Schema.Elements.Keys.ToList() };

        var files = ModelGenerator.Generate(config);

        Assert.Equal(Schema.Elements.Count + 3, files.Count); // one per element, plus enums, enum converters and registration
    }
}
