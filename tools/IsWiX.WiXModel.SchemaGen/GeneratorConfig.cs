using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using IsWiX.WiXModel.SchemaGen.CodeGen;
using IsWiX.WiXModel.SchemaGen.Schema;

namespace IsWiX.WiXModel.SchemaGen;

/// <summary>The contents of <c>schemagen.json</c>. Paths are relative to the file.</summary>
internal sealed record GeneratorConfig(
    string SchemaDirectory,
    string SchemaVersion,
    string OutputDirectory,
    string Namespace,
    IReadOnlyList<string> Elements)
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public string BaseDirectory { get; private init; } = string.Empty;

    public string FullSchemaDirectory => Path.GetFullPath(Path.Combine(BaseDirectory, SchemaDirectory));

    public string FullOutputDirectory => Path.GetFullPath(Path.Combine(BaseDirectory, OutputDirectory));

    public static GeneratorConfig Load(string path)
    {
        var config = JsonSerializer.Deserialize<GeneratorConfig>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException($"{path} is empty.");
        return config with { BaseDirectory = Path.GetDirectoryName(Path.GetFullPath(path))! };
    }
}

/// <summary>Runs the generator in memory. Used by the command line and by the up-to-date test.</summary>
internal static class ModelGenerator
{
    public static IReadOnlyList<GeneratedFile> Generate(GeneratorConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var schema = SchemaLoader.Load(config.FullSchemaDirectory);
        return new ModelEmitter(schema, config).Emit();
    }
}
