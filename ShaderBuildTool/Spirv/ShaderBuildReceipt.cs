using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ShaderBuildTool.Spirv;

/// <summary>Tracks the complete source/tool input set and exact generated output set, including deletions and missing variants.</summary>
internal static class ShaderBuildReceipt
{
    /// <summary>Content hashes of all published files; temporary compiler inputs are excluded.</summary>
    private sealed record Receipt(int Version, string Inputs, Dictionary<string, string> Outputs, Dictionary<string, string>? InputDetails = null, string? BaseInputs = null, ShaderInputObservation[]? ConsumedInputs = null);

    #region Public API
    #region Input identities
    /// <summary>Hashes source paths and contents against the invocation's compiler identity, including removed inputs.</summary>
    public static string Fingerprint(string assetsRoot, string domain, string compilerIdentity, ShaderFileHashIndex? index = null,
        Dictionary<string, string>? details = null)
    {
        var inputs = Directory.EnumerateFiles(Path.Combine(assetsRoot, domain), "*", SearchOption.AllDirectories)
            .Where(p => p.Contains(Path.DirectorySeparatorChar + "shaders" + Path.DirectorySeparatorChar)
                || p.Contains(Path.DirectorySeparatorChar + "shaderincludes" + Path.DirectorySeparatorChar))
            .Select(Path.GetFullPath).Order(StringComparer.Ordinal);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(compilerIdentity));
        foreach (string path in inputs)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(path));
            byte[] digest = HashInput(path, index);
            hash.AppendData(digest);
            if (details is not null) details["shader: " + path] = Convert.ToHexString(digest);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    /// <summary>Computes compiler contents and effective invocation policy independently of catalogue and processing assemblies.</summary>
    public static string CompilerFingerprint(string workingDirectory, string target, bool warnings, ShaderFileHashIndex? index = null,
        Dictionary<string, string>? details = null)
    {
        var inputs = CompilerInputs(workingDirectory);
        string policy = JsonSerializer.Serialize(ShaderCompilerProcess.PolicyArguments("<stage>", target, warnings, "<entry-point>"));
        var identity = new List<string> { "compiler-identity-v2", policy };
        if (details is not null) details["compiler policy"] = policy;
        foreach (string path in inputs)
        {
            string digest = Convert.ToHexString(HashInput(path, index));
            identity.Add(path); identity.Add(digest);
            if (details is not null) details["compiler/tool: " + path] = digest;
        }
        return ShaderBuildIdentities.Hash(identity.ToArray());
    }

    /// <summary>Enumerates the pinned compiler package and manifest without recomputing their identity.</summary>
    internal static IEnumerable<string> CompilerInputs(string workingDirectory)
    {
        string toolManifest = Path.Combine(workingDirectory, ".config", "dotnet-tools.json");
        using var configuration = JsonDocument.Parse(File.ReadAllText(toolManifest));
        string version = configuration.RootElement.GetProperty("tools").GetProperty("dotnet-shaderc").GetProperty("version").GetString()!;
        string packageRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        string compilerRoot = Path.Combine(packageRoot, "dotnet-shaderc", version, "tools");
        if (!Directory.Exists(compilerRoot)) throw new DirectoryNotFoundException("Restore the pinned shader compiler before building: " + compilerRoot);
        // Include managed and native compiler contents, not just its version label: replacing either invalidates the receipt.
        return Directory.EnumerateFiles(compilerRoot, "*", SearchOption.AllDirectories)
            .Append(toolManifest).Select(Path.GetFullPath).Order(StringComparer.Ordinal);
    }

    #endregion

    #region Receipt validation
    /// <summary>Accepts only a complete previous success whose binaries and sidecars still match their recorded content.</summary>
    public static bool IsCurrent(string outputRoot, string fingerprint, Dictionary<string, string>? details = null,
        Action<string>? report = null)
    {
        string path = Path.Combine(outputRoot, "build-receipt.json");
        if (!File.Exists(path)) { report?.Invoke("No successful build receipt exists: " + path); return false; }
        try
        {
            var receipt = JsonSerializer.Deserialize<Receipt>(File.ReadAllText(path));
            if (receipt is null || receipt.Version != 2 || receipt.Outputs is not { Count: > 0 })
            {
                report?.Invoke("Build receipt is empty or incomplete: " + path);
                return false;
            }
            if (receipt.Inputs != fingerprint)
            {
                report?.Invoke($"Input fingerprint changed: {receipt.Inputs} -> {fingerprint}");
                ReportChanges(receipt.InputDetails, details, report);
                return false;
            }
            foreach (var pair in receipt.Outputs)
            {
                string file = ShaderPublicationPaths.FileWithin(outputRoot, pair.Key);
                if (!File.Exists(file)) { report?.Invoke("Published output missing: " + file); return false; }
                if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))) != pair.Value)
                { report?.Invoke("Published output content changed: " + file); return false; }
            }
            var published = PublishedFiles(outputRoot).Where(p => p != path)
                .Select(p => Path.GetRelativePath(outputRoot, p)).ToHashSet(StringComparer.Ordinal);
            if (!published.SetEquals(receipt.Outputs.Keys))
            {
                foreach (string added in published.Except(receipt.Outputs.Keys)) report?.Invoke("Unexpected published output: " + added);
                return false;
            }
            return true;
        }
        catch (ArgumentException error) { report?.Invoke("Malformed build receipt path: " + error.Message); return false; }
        catch (InvalidDataException error) { report?.Invoke("Invalid build receipt path: " + error.Message); return false; }
        catch (IOException error) { report?.Invoke("Unreadable build receipt: " + error.Message); return false; }
        catch (JsonException error) { report?.Invoke("Malformed build receipt: " + error.Message); return false; }
    }

    /// <summary>Validates the compact receipt input snapshot before opening any processing records.</summary>
    internal static bool TryReuse(string outputRoot, string baseFingerprint, ShaderFileHashIndex hashes,
        Dictionary<string, string> details, out ShaderInputObservation[] inputs, Action<string>? report = null)
    {
        inputs = [];
        try
        {
            var receipt = JsonSerializer.Deserialize<Receipt>(File.ReadAllBytes(Path.Combine(outputRoot, "build-receipt.json")));
            if (receipt is not { Version: 2, ConsumedInputs: not null, BaseInputs: not null })
            { report?.Invoke("Receipt input snapshot is missing or incompatible."); return false; }
            if (receipt.BaseInputs != baseFingerprint)
            {
                report?.Invoke($"Base input fingerprint changed: {receipt.BaseInputs} -> {baseFingerprint}");
                var prior = receipt.InputDetails?.Where(pair => !pair.Key.StartsWith("consumed: ", StringComparison.Ordinal))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                ReportChanges(prior, details, report);
                return false;
            }
            var observed = receipt.ConsumedInputs;
            if (observed.Any(input => input is null || !Path.IsPathFullyQualified(input.Path)
                || string.IsNullOrWhiteSpace(input.Resource) || !ShaderRecordStore.IsDigest(input.Hash))
                || observed.Select(input => input.Path).Distinct(StringComparer.Ordinal).Count() != observed.Length
                || CompleteFingerprint(baseFingerprint, observed) != receipt.Inputs)
            { report?.Invoke("Receipt dependency snapshot is incomplete or invalid."); return false; }
            // The combined fingerprint checks the stored dependency inventory; current hashes
            // then detect external-domain edits without reconstructing dependency graphs or syntax trees.
            foreach (var input in observed)
                if (Convert.ToHexString(hashes.GetHash(input.Path)) != input.Hash)
                { report?.Invoke("Consumed shader dependency changed: " + input.Path); return false; }
            if (!IsCurrent(outputRoot, receipt.Inputs, details, report)) return false;
            inputs = observed;
            return true;
        }
        catch (Exception error) when (error is IOException or JsonException or ArgumentException or UnauthorizedAccessException)
        { report?.Invoke("Receipt input snapshot unavailable: " + error.Message); return false; }
    }

    /// <summary>Combines current catalogue inputs with the exact consumed dependency inventory.</summary>
    internal static string CompleteFingerprint(string fingerprint, IEnumerable<ShaderInputObservation> inputs,
        Dictionary<string, string>? details = null)
    {
        var values = new List<string> { "complete-inputs-v1", fingerprint };
        foreach (var input in inputs.OrderBy(input => input.Path, StringComparer.Ordinal))
        {
            values.Add(input.Path); values.Add(input.Resource); values.Add(input.Hash);
            if (details is not null) details["consumed: " + input.Path] = input.Hash;
        }
        return ShaderBuildIdentities.Hash(values.ToArray());
    }

    #endregion

    #region Publication
    /// <summary>Publishes the receipt last; failed compilations can never become a successful incremental build.</summary>
    public static void Publish(string outputRoot, string fingerprint, Dictionary<string, string>? details = null,
        string? baseFingerprint = null, ShaderInputObservation[]? consumedInputs = null)
    {
        var outputs = PublishedFiles(outputRoot)
            .Where(p => Path.GetFileName(p) != "build-receipt.json")
            .ToDictionary(p => Path.GetRelativePath(outputRoot, p), p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))), StringComparer.Ordinal);
        // Keep incomplete metadata outside the published tree; the output lease owns this temporary name.
        string pending = Path.Combine(outputRoot, "_tmp", "build-receipt.pending");
        Directory.CreateDirectory(Path.GetDirectoryName(pending)!);
        try
        {
            File.WriteAllText(pending, JsonSerializer.Serialize(new Receipt(2, fingerprint, outputs, details, baseFingerprint, consumedInputs)));
            File.Move(pending, Path.Combine(outputRoot, "build-receipt.json"), overwrite: true);
        }
        finally { File.Delete(pending); }
    }

    #endregion
    #endregion

    #region Private
    /// <summary>Reports changed input identities while omitting unchanged entries.</summary>
    private static void ReportChanges(Dictionary<string, string>? before, Dictionary<string, string>? after, Action<string>? report)
    {
        if (before is null || after is null)
        { report?.Invoke("Previous receipt has no per-input diagnostics; the next successful build will record them."); return; }
        foreach (string key in before.Keys.Union(after.Keys).Order(StringComparer.Ordinal))
        {
            before.TryGetValue(key, out string? oldValue);
            after.TryGetValue(key, out string? newValue);
            if (oldValue != newValue)
                report?.Invoke($"Input {(oldValue is null ? "added" : newValue is null ? "removed" : "changed")}: {key}; {oldValue ?? "<absent>"} -> {newValue ?? "<absent>"}");
        }
    }

    /// <summary>Uses the shared metadata index when available and otherwise streams input bytes without buffering entire tools.</summary>
    private static byte[] HashInput(string path, ShaderFileHashIndex? index)
    {
        if (index is not null) return index.GetHash(path);
        using var stream = File.OpenRead(path);
        return SHA256.HashData(stream);
    }

    /// <summary>Prunes private trees before traversal so historical cache size does not increase receipt enumeration work.</summary>
    private static IEnumerable<string> PublishedFiles(string root, bool topLevel = true)
    {
        foreach (string file in Directory.EnumerateFiles(root))
        {
            ShaderPublicationPaths.EnsureUnredirected(file);
            yield return file;
        }
        foreach (string directory in Directory.EnumerateDirectories(root))
        {
            string name = Path.GetFileName(directory);
            if (topLevel && (name.Equals("_tmp", StringComparison.OrdinalIgnoreCase) || name.Equals("_cache", StringComparison.OrdinalIgnoreCase))) continue;
            if (name.StartsWith(".shader-pending-", StringComparison.Ordinal) || name.StartsWith(".shader-previous-", StringComparison.Ordinal)) continue;
            ShaderPublicationPaths.EnsureUnredirected(directory);
            foreach (string file in PublishedFiles(directory, topLevel: false)) yield return file;
        }
    }
    #endregion
}
