using System;
using System.IO;
using System.Linq;
using IsWiX.WiXModel.Core;
using IsWiX.WiXModel.SchemaGen;
using Xunit;
using IODirectory = System.IO.Directory;
using IOFile = System.IO.File;

namespace IsWiX.WiXModel.Tests;

public class GeneratorTests
{
    [Fact]
    public void Generated_code_is_up_to_date()
    {
        var config = GeneratorConfig.Load(Path.Combine(RepositoryRoot(), "src", "IsWiX.WiXModel", "schemagen.json"));

        var files = ModelGenerator.Generate(config);

        var onDisk = IODirectory.EnumerateFiles(config.FullOutputDirectory, "*.g.cs").Select(Path.GetFileName).Order(StringComparer.Ordinal);
        Assert.Equal(files.Select(f => f.FileName), onDisk);
        foreach (var file in files)
        {
            var existing = IOFile.ReadAllText(Path.Combine(config.FullOutputDirectory, file.FileName)).Replace("\r\n", "\n", StringComparison.Ordinal);
            Assert.True(
                existing == file.Content,
                $"{file.FileName} is out of date. Run: dotnet run --project tools/IsWiX.WiXModel.SchemaGen -- src/IsWiX.WiXModel/schemagen.json");
        }
    }

    [Theory]
    [InlineData(typeof(Package), nameof(Package.Name), typeof(string))]
    [InlineData(typeof(Package), nameof(Package.Scope), typeof(WixValue<PackageScopeType>?))]
    [InlineData(typeof(Package), nameof(Package.InstallerVersion), typeof(WixValue<int>?))]
    [InlineData(typeof(Component), nameof(Component.KeyPath), typeof(WixValue<bool>?))]
    [InlineData(typeof(Component), nameof(Component.Guid), typeof(string))]
    [InlineData(typeof(Component), nameof(Component.Location), typeof(WixValue<ComponentLocation>?))]
    [InlineData(typeof(File), nameof(File.Compressed), typeof(WixValue<YesNoDefaultType>?))]
    [InlineData(typeof(Directory), nameof(Directory.DiskId), typeof(WixValue<int>?))]
    [InlineData(typeof(Wix), nameof(Wix.RequiredVersion), typeof(string))]
    public void Schema_types_map_to_csharp_types(Type element, string property, Type expected)
    {
        Assert.Equal(expected, element.GetProperty(property)!.PropertyType);
    }

    [Fact]
    public void Only_schema_allowed_children_get_typed_accessors()
    {
        Assert.NotNull(typeof(Directory).GetProperty("Directories"));
        Assert.NotNull(typeof(Directory).GetProperty("Components"));
        Assert.NotNull(typeof(Component).GetProperty("Files"));
        Assert.Null(typeof(Component).GetProperty("Components"));
        Assert.Null(typeof(File).GetProperty("Files"));
    }

    internal static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (System.IO.File.Exists(Path.Combine(directory.FullName, "IsWiX.WiXModel.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not find IsWiX.WiXModel.slnx above the test output folder.");
    }
}
