using System.Security.Cryptography;
using VanillaGraphicsExpanded.Rendering.Spirv;
using System.Text;
using System.Diagnostics;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace ShaderBuildTool.Spirv;

/// <summary>Compiles deduplicated program projections directly to runtime SPIR-V assets.</summary>
internal static class ShaderVariantBuild
{
    #region Build and publication
    /// <summary>Publishes compiler output unchanged; program assignments and shared stages come from the resolver.</summary>
    public static async Task RunAsync(string assetsRoot, string outputRoot, string domain, string workingDirectory,
        string target, bool warningsAsErrors, ShaderVariantResolver registry, int concurrency, CancellationToken cancellationToken)
    {
        var elapsed = Stopwatch.StartNew();
        string manifestPath = Path.Combine(outputRoot, domain, "shaders", ShaderBinaryDigest.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        File.Delete(manifestPath);
        var digests = new System.Collections.Concurrent.ConcurrentDictionary<string, ShaderBinaryDigest.Entry>(StringComparer.Ordinal);
        ValidateSources(Path.Combine(assetsRoot, domain, "shaders"), registry);
        var sources = new ShaderVariantSource(assetsRoot, domain);
        var expanded = new Dictionary<string, string>(StringComparer.Ordinal);
        Console.WriteLine($"[SPIR-V] Programs: {registry.Programs.Count} programs, {registry.Programs.Values.Sum(p => p.Assignments.Count)} combinations");
        // Expand each source once; workers share only immutable text, never editable syntax trees.
        foreach (string source in registry.Binaries.Select(selection => selection.Stage.Source).Distinct(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            expanded.Add(source, sources.Expand(source));
        }
        double expansionMilliseconds = elapsed.Elapsed.TotalMilliseconds;
        long emissionTicks = 0, compilerTicks = 0;
        var jobs = new List<ShaderCompilationJob>();
        var outputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var selection in registry.Binaries)
        {
            var stage = selection.Stage;
            string binary = Path.Combine(outputRoot, domain, "shaders", selection.BinaryPath);
            if (!outputs.Add(Path.GetFullPath(binary)))
                throw new InvalidOperationException("Duplicate shader binary output: " + binary);
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(selection.Key))).ToLowerInvariant();
            string input = Path.Combine(outputRoot, "_tmp", stage.Identity + "." + hash + ".glsl");
            string extension = StageExtension(stage.Kind);
            jobs.Add(new ShaderCompilationJob($"stage '{stage.Identity}', source '{stage.Source}', configuration '{selection.Key}'", CompileVariantAsync));

            /// <summary>Emits, compiles and publishes this variant while recording work and removing partial output.</summary>
            async Task<ShaderCompilerResult> CompileVariantAsync(CancellationToken token)
            {
                string pending = input + ".spv";
                Directory.CreateDirectory(Path.GetDirectoryName(input)!);
                Directory.CreateDirectory(Path.GetDirectoryName(binary)!);
                File.Delete(binary + ".sha256"); // Remove legacy per-variant metadata during migration.
                File.Delete(binary);
                File.Delete(pending);
                try
                {
                    var timer = Stopwatch.StartNew();
                    var emitter = new ShaderVariantSource(assetsRoot, domain);
                    File.WriteAllText(input, ShaderSourceLayout.Apply(emitter.Emit(expanded[stage.Source], selection), extension, stage.Bindings));
                    Interlocked.Add(ref emissionTicks, timer.ElapsedTicks);
                    token.ThrowIfCancellationRequested();
                    timer.Restart();
                    var result = await ShaderCompilerProcess.CompileAsync(workingDirectory, input, pending,
                        Program.StageFromExtension(extension), target, warningsAsErrors, stage.EntryPoint, token);
                    Interlocked.Add(ref compilerTicks, timer.ElapsedTicks);
                    token.ThrowIfCancellationRequested();
                    // Compiler failure can leave a partial file; publish only a successful invocation's output.
                    if (result.ExitCode == 0)
                    {
                        byte[] bytes = File.ReadAllBytes(pending);
                        var digest = new ShaderBinaryDigest.Entry(bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)));
                        File.Move(pending, binary, overwrite: true);
                        digests[selection.BinaryPath] = digest;
                    }
                    return result;
                }
                finally { File.Delete(pending); }
            }
        }
        await ShaderCompilationBatch.RunAsync(jobs, concurrency, Console.Out, Console.Error, cancellationToken);
        // Publish only after every variant succeeds; the build receipt subsequently covers this manifest too.
        File.WriteAllBytes(manifestPath, ShaderBinaryDigest.Encode(digests.ToDictionary(pair => pair.Key, pair => pair.Value)));
        foreach (var stage in registry.Binaries.GroupBy(s => s.Stage.Identity))
            Console.WriteLine($"[SPIR-V] {stage.Key}: {stage.Count()} structural variants");
        Console.WriteLine($"[SPIR-V] Stages: {registry.Stages.Count} stages, {registry.Binaries.Count} variants");
        Console.WriteLine(FormattableString.Invariant($"[SPIR-V] Timing: concurrency={concurrency}; expansionMs={expansionMilliseconds:F1}; emissionWorkMs={emissionTicks * 1000.0 / Stopwatch.Frequency:F1}; compilerWorkMs={compilerTicks * 1000.0 / Stopwatch.Frequency:F1}; elapsedMs={elapsed.Elapsed.TotalMilliseconds:F1}"));
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
