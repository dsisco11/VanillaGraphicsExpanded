using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ShaderBuildTool.Spirv;

/// <summary>Tracks the complete source/tool input set and exact generated output set, including deletions and missing variants.</summary>
internal static class ShaderBuildReceipt
{
    /// <summary>Content hashes of all published files; temporary compiler inputs are excluded.</summary>
    private sealed record Receipt(string Inputs, Dictionary<string, string> Outputs);

    #region Input and output validation
    /// <summary>Hashes source paths and contents plus compiler/build-tool identities, so removed inputs change the result.</summary>
    public static string Fingerprint(string assetsRoot, string domain, string workingDirectory, string target, bool warnings)
    {
        string toolManifest = Path.Combine(workingDirectory, ".config", "dotnet-tools.json");
        using var configuration = JsonDocument.Parse(File.ReadAllText(toolManifest));
        string version = configuration.RootElement.GetProperty("tools").GetProperty("dotnet-shaderc").GetProperty("version").GetString()!;
        string packageRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        string compilerRoot = Path.Combine(packageRoot, "dotnet-shaderc", version, "tools");
        if (!Directory.Exists(compilerRoot)) throw new DirectoryNotFoundException("Restore the pinned shader compiler before building: " + compilerRoot);
        // Include managed and native compiler contents, not just its version label: replacing either invalidates the receipt.
        var inputs = Directory.EnumerateFiles(Path.Combine(assetsRoot, domain), "*", SearchOption.AllDirectories)
            .Where(p => p.Contains(Path.DirectorySeparatorChar + "shaders" + Path.DirectorySeparatorChar) || p.Contains(Path.DirectorySeparatorChar + "shaderincludes" + Path.DirectorySeparatorChar))
            .Concat(Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll"))
            .Concat(Directory.EnumerateFiles(compilerRoot, "*", SearchOption.AllDirectories))
            .Append(toolManifest).Select(Path.GetFullPath).Order(StringComparer.Ordinal);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(target + "|" + warnings + "|" + ShaderCompilerProcess.OptimizationArgument
            + "|debug=" + ShaderCompilerProcess.GenerateDebugInfo));
        foreach (string path in inputs)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetFullPath(path)));
            hash.AppendData(SHA256.HashData(File.ReadAllBytes(path)));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    /// <summary>Accepts only a complete previous success whose binaries and sidecars still match their recorded content.</summary>
    public static bool IsCurrent(string outputRoot, string fingerprint)
    {
        string path = Path.Combine(outputRoot, "build-receipt.json");
        if (!File.Exists(path)) return false;
        try
        {
            var receipt = JsonSerializer.Deserialize<Receipt>(File.ReadAllText(path));
            return receipt?.Inputs == fingerprint && receipt.Outputs.Count > 0 && receipt.Outputs.All(pair =>
            {
                string file = Path.Combine(outputRoot, pair.Key);
                return File.Exists(file) && Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))) == pair.Value;
            }) && Directory.EnumerateFiles(outputRoot, "*", SearchOption.AllDirectories)
                .Where(p => !Path.GetRelativePath(outputRoot, p).StartsWith("_tmp" + Path.DirectorySeparatorChar) && p != path)
                .Select(p => Path.GetRelativePath(outputRoot, p)).ToHashSet(StringComparer.Ordinal).SetEquals(receipt.Outputs.Keys);
        }
        catch (JsonException) { return false; }
    }

    /// <summary>Publishes the receipt last; failed compilations can never become a successful incremental build.</summary>
    public static void Publish(string outputRoot, string fingerprint)
    {
        var outputs = Directory.EnumerateFiles(outputRoot, "*", SearchOption.AllDirectories)
            .Where(p => !Path.GetRelativePath(outputRoot, p).StartsWith("_tmp" + Path.DirectorySeparatorChar) && Path.GetFileName(p) != "build-receipt.json")
            .ToDictionary(p => Path.GetRelativePath(outputRoot, p), p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))), StringComparer.Ordinal);
        File.WriteAllText(Path.Combine(outputRoot, "build-receipt.json"), JsonSerializer.Serialize(new Receipt(fingerprint, outputs)));
    }
    #endregion
}
