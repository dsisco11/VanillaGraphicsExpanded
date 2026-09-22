using VanillaGraphicsExpanded.Rendering.Contracts;

namespace ShaderBuildTool.Spirv;

/// <summary>Compiles contract-declared stage variants directly to runtime SPIR-V assets.</summary>
internal static class ShaderVariantBuild
{
    #region Build and publication
    /// <summary>Publishes compiler output unchanged; no binary reflection or runtime metadata is generated.</summary>
    public static void Run(string assetsRoot, string outputRoot, string domain, string workingDirectory, string target, bool warningsAsErrors)
    {
        string sourceRoot = Path.Combine(assetsRoot, domain, "shaders");
        var sources = new ShaderVariantSource(assetsRoot, domain);
        int stageCount = 0, variantCount = 0;
        string[] extensions = [".vsh", ".fsh", ".gsh", ".tcsh", ".tesh", ".csh"];
        foreach (string file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            if (!extensions.Contains(Path.GetExtension(file)) || Path.GetRelativePath(sourceRoot, file).StartsWith("includes" + Path.DirectorySeparatorChar)) continue;
            string relative = Path.GetRelativePath(sourceRoot, file).Replace('\\', '/');
            string expanded = sources.Expand(relative);
            string stage = Path.GetExtension(relative)[1..];
            var choices = GpuShaderContracts.CreateStage(relative);
            var bindings = GpuShaderContracts.Create(Path.ChangeExtension(relative, null));
            int count = 0;
            foreach (var configuration in choices.Variants())
            {
                string binary = Path.Combine(outputRoot, domain, "shaders", choices.BinaryPath(relative, configuration));
                string input = Path.Combine(outputRoot, "_tmp", relative + "." + LegacyShaderStageContract.Hash(choices.VariantKey(configuration)) + ".glsl");
                Directory.CreateDirectory(Path.GetDirectoryName(input)!);
                Directory.CreateDirectory(Path.GetDirectoryName(binary)!);
                File.WriteAllText(input, ShaderSourceLayout.Apply(sources.Emit(expanded, choices, configuration), stage, bindings));
                int status = Program.RunDotnetShaderc(workingDirectory, input, binary, Program.StageFromExtension(stage), target, warningsAsErrors);
                if (status != 0) throw new InvalidOperationException("SPIR-V compilation failed: " + relative);
                count++;
            }
            stageCount++;
            variantCount += count;
            Console.WriteLine($"[SPIR-V] {relative}: {count} structural variants");
        }
        Console.WriteLine($"[SPIR-V] Stages: {stageCount} stages, {variantCount} variants");
    }
    #endregion
}
