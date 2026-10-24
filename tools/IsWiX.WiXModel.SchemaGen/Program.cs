using System;
using System.IO;
using System.Linq;
using IsWiX.WiXModel.SchemaGen;

// Usage: dotnet run --project tools/IsWiX.WiXModel.SchemaGen -- src/IsWiX.WiXModel/schemagen.json
if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: IsWiX.WiXModel.SchemaGen <path to schemagen.json>");
    return 2;
}

try
{
    var config = GeneratorConfig.Load(args[0]);
    var files = ModelGenerator.Generate(config);

    Directory.CreateDirectory(config.FullOutputDirectory);
    var expected = files.Select(f => f.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
    foreach (var stale in Directory.EnumerateFiles(config.FullOutputDirectory, "*.g.cs").Where(f => !expected.Contains(Path.GetFileName(f))))
    {
        File.Delete(stale);
        Console.WriteLine($"Deleted {Path.GetFileName(stale)}");
    }

    foreach (var file in files)
    {
        File.WriteAllText(Path.Combine(config.FullOutputDirectory, file.FileName), file.Content);
    }

    Console.WriteLine($"Wrote {files.Count} files to {config.FullOutputDirectory}");
    return 0;
}
catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException or InvalidDataException or System.Text.Json.JsonException)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}
