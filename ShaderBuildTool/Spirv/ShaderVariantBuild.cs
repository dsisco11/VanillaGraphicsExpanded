using System.Security.Cryptography;
using System.Text;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace ShaderBuildTool.Spirv;

/// <summary>Compiles deduplicated program projections directly to runtime SPIR-V assets.</summary>
internal static class ShaderVariantBuild
{
    #region Build and publication
    /// <summary>Publishes compiler output unchanged; program assignments and shared stages come from the resolver.</summary>
    public static void Run(string assetsRoot, string outputRoot, string domain, string workingDirectory, string target, bool warningsAsErrors, ShaderVariantResolver? registry = null)
    {
        registry ??= GpuShaderContracts.Registry;
        ValidateSources(Path.Combine(assetsRoot, domain, "shaders"), registry);
        var sources = new ShaderVariantSource(assetsRoot, domain);
        var expanded = new Dictionary<string, string>(StringComparer.Ordinal);
        Console.WriteLine($"[SPIR-V] Programs: {registry.Programs.Count} programs, {registry.Programs.Values.Sum(p => p.Assignments.Count)} combinations");
        // Binaries already projects supported program assignments, retaining only unique stage/key pairs.
        // Cache expansion by source while preserving distinct stage identities and binding declarations.
        foreach (var selection in registry.Binaries)
        {
            var stage = selection.Stage;
            if (!expanded.TryGetValue(stage.Source, out string? source))
                expanded.Add(stage.Source, source = sources.Expand(stage.Source));
            string binary = Path.Combine(outputRoot, domain, "shaders", selection.BinaryPath);
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(selection.Key))).ToLowerInvariant();
            string input = Path.Combine(outputRoot, "_tmp", stage.Identity + "." + hash + ".glsl");
            Directory.CreateDirectory(Path.GetDirectoryName(input)!);
            Directory.CreateDirectory(Path.GetDirectoryName(binary)!);
            string extension = StageExtension(stage.Kind);
            File.WriteAllText(input, ShaderSourceLayout.Apply(sources.Emit(source, selection), extension, stage.Bindings));
            int status = Program.RunDotnetShaderc(workingDirectory, input, binary, Program.StageFromExtension(extension), target, warningsAsErrors, stage.EntryPoint);
            if (status != 0) throw new InvalidOperationException($"SPIR-V compilation failed: stage '{stage.Identity}', source '{stage.Source}', configuration '{selection.Key}'.");
        }
        foreach (var stage in registry.Binaries.GroupBy(s => s.Stage.Identity))
            Console.WriteLine($"[SPIR-V] {stage.Key}: {stage.Count()} structural variants");
        Console.WriteLine($"[SPIR-V] Stages: {registry.Stages.Count} stages, {registry.Binaries.Count} variants");
    }

    /// <summary>Checks owned entry-point coverage without inferring contracts or assuming source names are identities.</summary>
    internal static void ValidateSources(string sourceRoot, ShaderVariantResolver registry)
    {
        string[] extensions = [".vsh", ".fsh", ".gsh", ".tcsh", ".tesh", ".csh"];
        var declared = registry.Stages.Values.Select(s => s.Source).ToHashSet(StringComparer.Ordinal);
        var owned = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Where(file => extensions.Contains(Path.GetExtension(file)))
            .Select(file => Path.GetRelativePath(sourceRoot, file).Replace('\\', '/'))
            .Where(relative => !relative.StartsWith("includes/", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
        string[] unknown = owned.Except(declared).Order(StringComparer.Ordinal).ToArray();
        string[] missing = declared.Where(source => !File.Exists(Path.Combine(sourceRoot, source))).Order(StringComparer.Ordinal).ToArray();
        if (unknown.Length != 0 || missing.Length != 0)
            throw new InvalidOperationException($"Shader source coverage failed. Unregistered owned entry points: [{string.Join(", ", unknown)}]. Missing registered sources: [{string.Join(", ", missing)}].");
    }

    /// <summary>Maps the declared stage kind to the layout and compiler vocabulary, independently of asset naming.</summary>
    private static string StageExtension(ShaderStageKind kind) => kind switch
    {
        ShaderStageKind.Vertex => "vsh", ShaderStageKind.Fragment => "fsh", ShaderStageKind.Geometry => "gsh",
        ShaderStageKind.TessellationControl => "tcsh", ShaderStageKind.TessellationEvaluation => "tesh",
        ShaderStageKind.Compute => "csh", _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    #endregion
}
