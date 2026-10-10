using System.Diagnostics;
using ShaderBuildTool.Generation;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace ShaderBuildTool.Spirv;

/// <summary>Owns the leased build lifetime, input snapshot, selective receipt check and generation commit.</summary>
internal static class ShaderBuildInvocation
{
    #region Public API
    /// <summary>Builds one resolved catalogue, publishing success only after input and output validation.</summary>
    internal static async Task RunAsync(string assetsRoot, string outputRoot, string domain, string workingDirectory,
        string target, bool warnings, Func<ShaderVariantResolver> resolveRegistry, string scope, int concurrency,
        bool incremental, bool clean, bool verifyContents, CancellationToken cancellationToken)
    {
        using var lease = ShaderOutputLease.Acquire(outputRoot);
        var publication = new ShaderPublication(outputRoot, domain, report: Report);
        try
        {
            publication.Recover();
            string shaders = Path.Combine(assetsRoot, domain, "shaders");
            if (!Directory.Exists(shaders)) throw new DirectoryNotFoundException("Shader input directory is missing: " + shaders);
            LumonOctahedralShWeights.Generate(shaders);
            var registry = resolveRegistry();
            if (clean && Directory.Exists(outputRoot)) Directory.Delete(outputRoot, recursive: true);
            Directory.CreateDirectory(outputRoot);
            var timer = Stopwatch.StartNew();
            var hashes = new ShaderFileHashIndex(outputRoot, verifyContents || clean);
            var details = new Dictionary<string, string>(StringComparer.Ordinal) { ["registry scope"] = scope };
            var identities = ShaderBuildIdentities.Capture(workingDirectory, target, warnings, hashes, details);
            details["preprocessing identity"] = identities.Preprocessing;
            details["emission identity"] = identities.Emission;
            details["interface identity"] = identities.Interface;
            string fingerprint = ShaderBuildReceipt.Fingerprint(assetsRoot, domain, identities.Receipt(registry, scope, details), hashes, details);
            var snapshot = new ShaderBuildInputSnapshot(assetsRoot, domain, workingDirectory, hashes, registry);
            Report(FormattableString.Invariant($"Input hashes: reused={hashes.ReusedFiles}; read={hashes.HashedFiles}; elapsedMs={timer.Elapsed.TotalMilliseconds:F1}"));
            if (clean) Report("Rebuild reason: --clean explicitly discards outputs and cache.");
            else if (!incremental) Report("Rebuild reason: --incremental was not enabled.");
            cancellationToken.ThrowIfCancellationRequested();
            ShaderVariantBuild.ValidateSources(shaders, registry);
            if (incremental && !clean && ShaderBuildReceipt.TryReuse(outputRoot, fingerprint, hashes, details,
                out var consumed, reason => Report("Rebuild reason: " + reason)))
            {
                snapshot.Validate(consumed);
                cancellationToken.ThrowIfCancellationRequested();
                hashes.Save();
                Report("All shader binaries and contracts are current; processing records were not loaded.");
                new ShaderBuildStatistics
                {
                    RootsReused = registry.Binaries.Select(selection => selection.Stage.Source).Distinct(StringComparer.Ordinal).Count(),
                    VariantsReused = registry.Binaries.Count, CompilerReused = registry.Binaries.Count,
                    InterfacesReused = registry.Binaries.Count, OutputsRetained = registry.Binaries.Count, ReceiptReused = true
                }.Report(timer.Elapsed.TotalMilliseconds, 0, 0);
                return;
            }
            await ShaderVariantBuild.RunAsync(assetsRoot, outputRoot, domain, workingDirectory, target, warnings,
                registry, concurrency, cancellationToken, incremental && !clean, identities.Compiler,
                generation =>
                {
                    string completeFingerprint = ShaderBuildReceipt.CompleteFingerprint(fingerprint, generation.Inputs, details);
                    publication.Publish(generation.Binaries, generation.Manifest, completeFingerprint,
                        publishReceipt: () =>
                        {
                            hashes.Save();
                            ShaderBuildReceipt.Publish(outputRoot, completeFingerprint, details, fingerprint, generation.Inputs.ToArray());
                        }, validateInputs: () => snapshot.Validate(generation.Inputs), cancellationToken: cancellationToken);
                }, new ShaderBuildExecution(identities, hashes));
        }
        catch
        {
            if (Directory.Exists(outputRoot)) File.Delete(Path.Combine(outputRoot, "build-receipt.json"));
            throw;
        }
    }
    #endregion

    #region Private
    /// <summary>Prefixes invocation diagnostics consistently with shader processing output.</summary>
    private static void Report(string message) => Console.WriteLine("[SPIR-V] " + message);
    #endregion
}
