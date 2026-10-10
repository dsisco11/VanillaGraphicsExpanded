using VanillaGraphicsExpanded.Rendering.Spirv;

namespace ShaderBuildTool.Spirv;

/// <summary>Classifies binary publication against existing bytes and previous declared membership.</summary>
internal static class ShaderOutputStatistics
{
    #region Public API
    /// <summary>Counts unchanged, changed/new, damaged and removed binaries before a successful publication.</summary>
    internal static void Capture(string outputRoot, string domain, ShaderBuildGeneration generation, ShaderBuildStatistics statistics)
    {
        string active = Path.Combine(outputRoot, domain, "shaders");
        string manifestPath = Path.Combine(active, ShaderBinaryDigest.FileName);
        ShaderPublicationPaths.EnsureUnredirected(active);
        var prior = File.Exists(manifestPath) ? ShaderBinaryDigest.Parse(File.ReadAllBytes(manifestPath)) : null;
        foreach (var pair in generation.Binaries)
        {
            string file = ShaderPublicationPaths.FileWithin(active, pair.Key.Replace('/', Path.DirectorySeparatorChar));
            byte[]? bytes = File.Exists(file) ? File.ReadAllBytes(file) : null;
            if (bytes is not null && bytes.AsSpan().SequenceEqual(pair.Value)) statistics.OutputsRetained++;
            else if (prior?.Binaries.TryGetValue(pair.Key, out var previous) == true &&
                (bytes is null || previous is null || previous.Length != bytes.Length || previous.Digest != ShaderVariantCache.Digest(bytes).Digest))
                statistics.OutputsRepaired++;
            else statistics.OutputsReplaced++;
        }
        var oldPaths = Directory.Exists(active) ? Directory.EnumerateFiles(active, "*.spv", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(active, path).Replace('\\', '/')).ToHashSet(StringComparer.Ordinal) : new HashSet<string>(StringComparer.Ordinal);
        if (prior is not null) oldPaths.UnionWith(prior.Binaries.Keys);
        statistics.OutputsRemoved = oldPaths.Except(generation.Binaries.Keys).Count();
    }
    #endregion
}
