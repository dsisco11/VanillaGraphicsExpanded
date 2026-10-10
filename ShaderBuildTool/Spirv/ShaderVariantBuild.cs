using System.Collections.Immutable;
using System.Diagnostics;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace ShaderBuildTool.Spirv;

/// <summary>Plans selective source and variant processing before publishing a complete runtime generation.</summary>
internal static class ShaderVariantBuild
{
    #region Public API
    /// <summary>Reuses verified records before scheduling expensive work and returns actual work counters.</summary>
    public static async Task<ShaderBuildStatistics> RunAsync(string assetsRoot, string outputRoot, string domain, string workingDirectory,
        string target, bool warningsAsErrors, ShaderVariantResolver registry, int concurrency, CancellationToken cancellationToken,
        bool incremental = false, string? compilerIdentity = null, Action<ShaderBuildGeneration>? publish = null,
        ShaderBuildExecution? execution = null)
    {
        Directory.CreateDirectory(outputRoot);
        var timer = Stopwatch.StartNew();
        var publication = new ShaderPublication(outputRoot, domain, report: message => Console.WriteLine("[SPIR-V] " + message));
        publication.Recover();
        var statistics = new ShaderBuildStatistics();
        try
        {
            ValidateSources(Path.Combine(assetsRoot, domain, "shaders"), registry);
            byte[] contracts = ShaderContractProjection.Membership(registry, "selection");
            var hashes = execution?.Hashes ?? new ShaderFileHashIndex(outputRoot);
            var identities = execution?.Identities ?? ShaderBuildIdentities.Capture(workingDirectory, target, warningsAsErrors, hashes);
            var compiler = new ShaderVariantCache(outputRoot, compilerIdentity ?? identities.Compiler);
            var reasons = new System.Collections.Concurrent.ConcurrentDictionary<string, int>(StringComparer.Ordinal);
            /// <summary>Collects cache miss diagnostics from planning and concurrent processing.</summary>
            void Miss(string reason) => reasons.AddOrUpdate(reason, 1, (_, count) => count + 1);
            var expanded = ShaderSourcePlan.Create(assetsRoot, domain, outputRoot,
                registry.Binaries.Select(selection => selection.Stage.Source), identities.Preprocessing, hashes,
                incremental, statistics, Miss, cancellationToken);
            var observed = new Dictionary<string, ShaderInputObservation>(StringComparer.Ordinal);
            foreach (var source in expanded.Values)
                foreach (var input in source.Inputs)
                {
                    if (observed.TryGetValue(input.Path, out var earlier) && earlier.Hash != input.Hash)
                        throw new IOException("Shader input changed between roots: " + input.Path);
                    observed[input.Path] = input;
                }
            var processor = new ShaderVariantProcessor(assetsRoot, domain, outputRoot, workingDirectory, target,
                warningsAsErrors, incremental, identities, compiler, statistics, Miss);
            var selected = new List<ShaderCompilationJob>();
            var outputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var selection in registry.Binaries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!outputs.Add(selection.BinaryPath)) throw new InvalidOperationException("Duplicate shader binary output: " + selection.BinaryPath);
                var job = processor.Select(selection, expanded[selection.Stage.Source]);
                if (job is not null) selected.Add(job);
            }
            double plannedWorkMs = (statistics.ExpansionTicks + statistics.InterfaceTicks) * 1000.0 / Stopwatch.Frequency;
            double inputCheckMs = Math.Max(0, timer.Elapsed.TotalMilliseconds - plannedWorkMs);
            timer.Restart();
            Console.WriteLine($"[SPIR-V] Reused {statistics.RootsReused} roots and {statistics.VariantsReused} variant records before scheduling.");
            Console.WriteLine($"[SPIR-V] Selected {selected.Count}/{registry.Binaries.Count} variants for emission; concurrency={concurrency}.");
            await ShaderCompilationBatch.RunAsync(selected, concurrency, Console.Out, Console.Error, cancellationToken);
            if (processor.Binaries.Count != registry.Binaries.Count || processor.Digests.Count != registry.Binaries.Count)
                throw new InvalidDataException("Selected processing did not produce complete catalogue membership.");
            var generation = new ShaderBuildGeneration(processor.Binaries,
                ShaderBinaryDigest.Encode(processor.Digests.ToDictionary(pair => pair.Key, pair => pair.Value)),
                observed.Values.OrderBy(input => input.Path, StringComparer.Ordinal).ToImmutableArray());
            double processingMs = timer.Elapsed.TotalMilliseconds + plannedWorkMs;
            timer.Restart();
            if (!contracts.AsSpan().SequenceEqual(ShaderContractProjection.Membership(registry, "selection")))
                throw new IOException("Shader contracts changed during processing; rerun the build.");
            ShaderOutputStatistics.Capture(outputRoot, domain, generation, statistics);
            if (publish is not null) publish(generation);
            else publication.Publish(generation.Binaries, generation.Manifest,
                validateInputs: () => generation.ValidateInputs(new ShaderFileHashIndex(outputRoot, true)), cancellationToken: cancellationToken);
            hashes.Save();
            statistics.Report(inputCheckMs, processingMs, timer.Elapsed.TotalMilliseconds);
            foreach (var reason in reasons.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                Console.WriteLine($"[SPIR-V] Cache miss reason: {reason.Key}; occurrences={reason.Value}");
            return statistics;
        }
        catch
        {
            File.Delete(Path.Combine(outputRoot, "build-receipt.json"));
            throw;
        }
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
}
