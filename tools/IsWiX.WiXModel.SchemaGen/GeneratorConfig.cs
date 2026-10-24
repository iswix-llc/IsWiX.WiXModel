using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using IsWiX.WiXModel.SchemaGen.CodeGen;
using IsWiX.WiXModel.SchemaGen.Schema;

namespace IsWiX.WiXModel.SchemaGen;

/// <summary>
/// The contents of <c>schemagen.json</c> (core WiX) or <c>schemagen.&lt;extension&gt;.json</c>. Paths are relative to
/// the file. An extension names the core config as its <see cref="Base"/>: its attributes may use core types, and its
/// elements' core parents get typed accessors prefixed with <see cref="MemberPrefix"/>.
/// </summary>
internal sealed record GeneratorConfig(
    string SchemaDirectory,
    string SchemaVersion,
    string OutputDirectory,
    string Namespace,
    IReadOnlyList<string> Elements,
    string? Base = null,
    string? MemberPrefix = null)
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public string BaseDirectory { get; private init; } = string.Empty;

    /// <summary>Gets the config file's path relative to the repository root, as the generated headers cite it.</summary>
    public string FileName { get; private init; } = string.Empty;

    public string FullSchemaDirectory => Path.GetFullPath(Path.Combine(BaseDirectory, SchemaDirectory));

    public string FullOutputDirectory => Path.GetFullPath(Path.Combine(BaseDirectory, OutputDirectory));

    public static GeneratorConfig Load(string path)
    {
        var config = JsonSerializer.Deserialize<GeneratorConfig>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException($"{path} is empty.");
        return config with
        {
            BaseDirectory = Path.GetDirectoryName(Path.GetFullPath(path))!,
            FileName = "src/IsWiX.WiXModel/" + Path.GetFileName(path),
        };
    }

    /// <summary>Loads the config named by <see cref="Base"/>, or returns <see langword="null"/>.</summary>
    public GeneratorConfig? LoadBase() => Base is null ? null : Load(Path.Combine(BaseDirectory, Base));
}

/// <summary>Runs the generator in memory. Used by the command line and by the up-to-date test.</summary>
internal static class ModelGenerator
{
    public static IReadOnlyList<GeneratedFile> Generate(GeneratorConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var schema = SchemaLoader.Load(config.FullSchemaDirectory);
        var baseConfig = config.LoadBase();
        var baseSchema = baseConfig is null ? null : SchemaLoader.Load(baseConfig.FullSchemaDirectory);
        return new ModelEmitter(schema, config, baseSchema, baseConfig).Emit();
    }
}
