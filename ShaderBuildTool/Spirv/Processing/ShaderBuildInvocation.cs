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
            await ShaderVariantBuild.RunAsync(assetsRoot, outputRoot, domain, workingDirectory, target, warnings,
                registry, concurrency, cancellationToken, incremental && !clean, identities.Compiler,
                generation =>
                {
                    string completeFingerprint = CompleteFingerprint(fingerprint, generation, details);
                    publication.Publish(generation.Binaries, generation.Manifest, completeFingerprint,
                        publishReceipt: () =>
                        {
                            hashes.Save();
                            ShaderBuildReceipt.Publish(outputRoot, completeFingerprint, details);
                        }, validateInputs: () => snapshot.Validate(generation), cancellationToken: cancellationToken);
                }, new ShaderBuildExecution(identities, hashes, generation =>
                {
                    string completeFingerprint = CompleteFingerprint(fingerprint, generation, details);
                    if (!ShaderBuildReceipt.IsCurrent(outputRoot, completeFingerprint, details, reason => Report("Rebuild reason: " + reason))) return false;
                    snapshot.Validate(generation);
                    // The receipt and current record generation must describe the same bytes and membership.
                    if (!MatchesGeneration(outputRoot, domain, generation)) return false;
                    Report("All shader binaries and contracts are current.");
                    return true;
                }));
        }
        catch
        {
            if (Directory.Exists(outputRoot)) File.Delete(Path.Combine(outputRoot, "build-receipt.json"));
            throw;
        }
    }
    #endregion

    #region Private
    /// <summary>Includes every consumed dependency, including resources resolved outside the owning asset domain.</summary>
    private static string CompleteFingerprint(string fingerprint, ShaderBuildGeneration generation, Dictionary<string, string> details)
    {
        var values = new List<string> { "complete-inputs-v1", fingerprint };
        foreach (var input in generation.Inputs.OrderBy(input => input.Path, StringComparer.Ordinal))
        {
            values.Add(input.Path); values.Add(input.Resource); values.Add(input.Hash);
            details["consumed: " + input.Path] = input.Hash;
        }
        return ShaderBuildIdentities.Hash(values.ToArray());
    }

    /// <summary>Rejects a receipt whose published generation differs from the selected records.</summary>
    private static bool MatchesGeneration(string outputRoot, string domain, ShaderBuildGeneration generation)
    {
        string active = Path.Combine(outputRoot, domain, "shaders");
        var expected = generation.Binaries.Keys.Append(VanillaGraphicsExpanded.Rendering.Spirv.ShaderBinaryDigest.FileName).ToHashSet(StringComparer.Ordinal);
        var actual = Directory.EnumerateFiles(active, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(active, path).Replace('\\', '/')).ToHashSet(StringComparer.Ordinal);
        if (!expected.SetEquals(actual)) return false;
        if (!File.ReadAllBytes(Path.Combine(active, VanillaGraphicsExpanded.Rendering.Spirv.ShaderBinaryDigest.FileName)).AsSpan().SequenceEqual(generation.Manifest)) return false;
        return generation.Binaries.All(pair => File.ReadAllBytes(ShaderPublicationPaths.FileWithin(active, pair.Key.Replace('/', Path.DirectorySeparatorChar))).AsSpan().SequenceEqual(pair.Value));
    }

    /// <summary>Prefixes invocation diagnostics consistently with shader processing output.</summary>
    private static void Report(string message) => Console.WriteLine("[SPIR-V] " + message);
    #endregion
}
