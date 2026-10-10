namespace ShaderBuildTool.Spirv;

/// <summary>Selects invalid roots through restored TinyPreprocessor graphs before expanding any source.</summary>
internal static class ShaderSourcePlan
{
    #region Public API
    /// <summary>Checks each distinct recorded input once and replaces only affected successful root snapshots.</summary>
    internal static Dictionary<string, ShaderExpandedSource> Create(string assetsRoot, string domain, string outputRoot,
        IEnumerable<string> roots, string preprocessing, ShaderFileHashIndex hashes, bool incremental,
        ShaderBuildStatistics statistics, Action<string> miss, CancellationToken cancellationToken)
    {
        var preprocessor = new ShaderSourcePreprocessor(assetsRoot, domain);
        var cache = new ShaderExpandedSourceCache(outputRoot);
        var planner = new ShaderDependencyPlanner();
        var snapshots = new Dictionary<string, ShaderExpandedSource>(StringComparer.Ordinal);
        var changed = new HashSet<string>(StringComparer.Ordinal);
        string[] membership = roots.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        foreach (string relative in membership)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!incremental || !cache.TryLoadSnapshot(assetsRoot, preprocessor.RootId(relative), preprocessing, out var source, miss)) continue;
            snapshots.Add(relative, source);
            planner.Replace(source);
            foreach (var input in source.Inputs)
            {
                try
                {
                    if (Convert.ToHexString(hashes.GetHash(input.Path)) != input.Hash)
                    { changed.Add(input.Resource); miss("dependency changed: " + input.Resource); }
                }
                catch (IOException) { changed.Add(input.Resource); }
            }
        }
        var affected = planner.Affected(assetsRoot, changed).Select(source => source.Root).ToHashSet(StringComparer.Ordinal);
        foreach (string relative in membership)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (snapshots.TryGetValue(relative, out var source) && !affected.Contains(source.Root))
            { statistics.RootsReused++; continue; }
            File.Delete(Path.Combine(outputRoot, "build-receipt.json"));
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            source = preprocessor.Expand(relative);
            statistics.ExpansionTicks += System.Diagnostics.Stopwatch.GetTimestamp() - start;
            cache.Store(source, preprocessing);
            snapshots[relative] = source;
            planner.Replace(source);
            statistics.RootsExpanded++;
        }
        return snapshots;
    }
    #endregion
}
