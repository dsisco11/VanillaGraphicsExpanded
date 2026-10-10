using System.Collections.Immutable;
using VanillaGraphicsExpanded.Rendering.Spirv;
using System.Diagnostics;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace ShaderBuildTool.Spirv;

/// <summary>Compiles deduplicated program projections directly to runtime SPIR-V assets.</summary>
internal static class ShaderVariantBuild
{
    #region Public API
    /// <summary>Publishes compiler output unchanged; program assignments and shared stages come from the resolver.</summary>
    public static async Task RunAsync(string assetsRoot, string outputRoot, string domain, string workingDirectory,
        string target, bool warningsAsErrors, ShaderVariantResolver registry, int concurrency, CancellationToken cancellationToken,
        bool incremental = false, string? compilerIdentity = null, Action<ShaderBuildGeneration>? publish = null)
    {
        var elapsed = Stopwatch.StartNew();
        var publication = new ShaderPublication(outputRoot, domain, report: message => Console.WriteLine("[SPIR-V] " + message));
        publication.Recover();
        File.Delete(Path.Combine(outputRoot, "build-receipt.json"));
        string manifestPath = Path.Combine(outputRoot, domain, "shaders", ShaderBinaryDigest.FileName);

        var digests = new System.Collections.Concurrent.ConcurrentDictionary<string, ShaderBinaryDigest.Entry>(StringComparer.Ordinal);
        var binaries = new System.Collections.Concurrent.ConcurrentDictionary<string, byte[]>(StringComparer.Ordinal);
        ValidateSources(Path.Combine(assetsRoot, domain, "shaders"), registry);
        var sources = new ShaderSourcePreprocessor(assetsRoot, domain);
        var observedInputs = new Dictionary<string, ShaderInputObservation>(StringComparer.Ordinal);
        var cache = new ShaderVariantCache(outputRoot, compilerIdentity
            ?? ShaderBuildReceipt.CompilerFingerprint(workingDirectory, target, warningsAsErrors));
        int hits = 0, misses = 0;
        var missReasons = new System.Collections.Concurrent.ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        var expanded = new Dictionary<string, string>(StringComparer.Ordinal);
        Console.WriteLine($"[SPIR-V] Programs: {registry.Programs.Count} programs, {registry.Programs.Values.Sum(p => p.Assignments.Count)} combinations");
        // Expand each source once; workers share only immutable text, never editable syntax trees.
        Console.WriteLine("[SPIR-V] Expanding shader sources...");
        foreach (string source in registry.Binaries.Select(selection => selection.Stage.Source).Distinct(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var processed = sources.Expand(source);
            expanded.Add(source, processed.Text);
            foreach (var inputObservation in processed.Inputs)
            {
                if (observedInputs.TryGetValue(inputObservation.Path, out var earlier) && earlier.Hash != inputObservation.Hash)
                    throw new IOException("Shader input changed between roots: " + inputObservation.Path);
                observedInputs[inputObservation.Path] = inputObservation;
            }
        }
        double expansionMilliseconds = elapsed.Elapsed.TotalMilliseconds;
        long emissionTicks = 0, compilerTicks = 0, interfaceTicks = 0;
        var jobs = new List<ShaderCompilationJob>();
        var outputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var selection in registry.Binaries)
        {
            var stage = selection.Stage;
            string binary = Path.Combine(outputRoot, domain, "shaders", selection.BinaryPath);
            if (!outputs.Add(Path.GetFullPath(binary)))
                throw new InvalidOperationException("Duplicate shader binary output: " + binary);
            string hash = ShaderVariantIdentifier.Create(selection.Key);
            string input = Path.Combine(outputRoot, "_tmp", stage.Identity + "." + hash + ".glsl");
            string extension = StageExtension(stage.Kind);
            jobs.Add(new ShaderCompilationJob($"stage '{stage.Identity}', source '{stage.Source}', configuration '{selection.Key}'", CompileVariantAsync));

            /// <summary>Emits, compiles and publishes this variant while recording work and removing partial output.</summary>
            async Task<ShaderCompilerResult> CompileVariantAsync(CancellationToken token)
            {
                string pending = input + ".spv";
                Directory.CreateDirectory(Path.GetDirectoryName(input)!);

                File.Delete(pending);
                try
                {
                    var timer = Stopwatch.StartNew();
                    var emitter = new ShaderVariantSource(assetsRoot, domain);
                    string source = ShaderSourceLayout.Apply(emitter.Emit(expanded[stage.Source], selection), extension, stage.Bindings);
                    string key = cache.Key(source, Program.StageFromExtension(extension), stage.EntryPoint, input, workingDirectory);
                    if (incremental && cache.TryRead(key, out var cachedBytes, out var cachedDigest,
                        reason => missReasons.AddOrUpdate(reason, 1, (_, count) => count + 1)))
                    {
                        Interlocked.Add(ref emissionTicks, timer.ElapsedTicks);
                        long interfaceStart = Stopwatch.GetTimestamp();
                        var metadata = ShaderInterfaceExtraction.Extract(cachedBytes, selection);
                        Interlocked.Add(ref interfaceTicks, Stopwatch.GetTimestamp() - interfaceStart);
                        binaries[selection.BinaryPath] = cachedBytes;
                        digests[selection.BinaryPath] = cachedDigest with { Interface = metadata };
                        Interlocked.Increment(ref hits);
                        return new ShaderCompilerResult(0, "", "");
                    }
                    Interlocked.Increment(ref misses);
                    if (!incremental) missReasons.AddOrUpdate("incremental cache reuse disabled", 1, (_, count) => count + 1);
                    File.WriteAllText(input, source);
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
                        var digest = ShaderVariantCache.Digest(bytes);
                        long interfaceStart = Stopwatch.GetTimestamp();
                        var metadata = ShaderInterfaceExtraction.Extract(bytes, selection);
                        Interlocked.Add(ref interfaceTicks, Stopwatch.GetTimestamp() - interfaceStart);
                        cache.Store(key, bytes, digest);
                        binaries[selection.BinaryPath] = bytes;
                        digests[selection.BinaryPath] = digest with { Interface = metadata };
                    }
                    return result;
                }
                finally { File.Delete(pending); }
            }
        }
        Console.WriteLine($"[SPIR-V] Processing {jobs.Count} variants (emission, cache verification, compilation on cache misses); concurrency={concurrency}...");
        await ShaderCompilationBatch.RunAsync(jobs, concurrency, Console.Out, Console.Error, cancellationToken);
        Console.WriteLine("[SPIR-V] Pruning obsolete outputs and publishing binary digests...");
        // Keep the previous coherent generation intact until every compiler and extractor succeeds.
        var generation = new ShaderBuildGeneration(binaries,
            ShaderBinaryDigest.Encode(digests.ToDictionary(pair => pair.Key, pair => pair.Value)), observedInputs.Values.ToImmutableArray());
        if (publish is not null) publish(generation);
        else publication.Publish(generation.Binaries, generation.Manifest, validateInputs: () => generation.ValidateInputs(new ShaderFileHashIndex(outputRoot, true)), cancellationToken: cancellationToken);
        Console.WriteLine(FormattableString.Invariant($"[SPIR-V] Interface extraction: workMs={interfaceTicks * 1000.0 / Stopwatch.Frequency:F1}; manifestBytes={new FileInfo(manifestPath).Length}"));
        foreach (var stage in registry.Binaries.GroupBy(s => s.Stage.Identity))
            Console.WriteLine($"[SPIR-V] {stage.Key}: {stage.Count()} structural variants");
        Console.WriteLine($"[SPIR-V] Stages: {registry.Stages.Count} stages, {registry.Binaries.Count} variants");
        Console.WriteLine($"[SPIR-V] Cache hits={hits}; misses={misses}; shadersRecompiled={misses}; compilerInvocations={misses}");
        foreach (var reason in missReasons.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            Console.WriteLine($"[SPIR-V] Cache miss reason: {reason.Key}; variants={reason.Value}");
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

    #endregion

    #region Private
    /// <summary>Maps the declared stage kind to the layout and compiler vocabulary, independently of asset naming.</summary>
    private static string StageExtension(ShaderStageKind kind) => kind switch
    {
        ShaderStageKind.Vertex => "vsh", ShaderStageKind.Fragment => "fsh", ShaderStageKind.Geometry => "gsh",
        ShaderStageKind.TessellationControl => "tcsh", ShaderStageKind.TessellationEvaluation => "tesh",
        ShaderStageKind.Compute => "csh", _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    #endregion
}
