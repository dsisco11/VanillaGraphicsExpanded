using System.Collections.Immutable;

namespace ShaderBuildTool.Spirv;

/// <summary>Owns current per-root snapshots during single-threaded planning; workers receive immutable data.</summary>
internal sealed class ShaderDependencyPlanner
{
    private readonly Dictionary<(string Assets, string Root), ShaderExpandedSource> roots = new();

    #region Public API
    /// <summary>Replaces only the named root's successful graph, retaining other roots' shared dependencies.</summary>
    internal void Replace(ShaderExpandedSource source)
    {
        ShaderExpandedSource.Validate(source);
        roots[(source.AssetsRoot, source.Root)] = source;
    }

    /// <summary>Forgets roots outside current membership without deleting historical cache artifacts.</summary>
    internal void Remove(string assetsRoot, string root) => roots.Remove((Path.GetFullPath(assetsRoot), root));

    /// <summary>Restores private library graphs and follows direct dependents to their owning roots.</summary>
    internal ImmutableArray<ShaderExpandedSource> Affected(string assetsRoot, IEnumerable<string> changedResources)
    {
        string context = Path.GetFullPath(assetsRoot);
        string[] changed = changedResources.Distinct(StringComparer.Ordinal).ToArray();
        // No mutable graph escapes this controlled planning query or becomes a second persisted authority.
        return roots.Values.Where(source => source.AssetsRoot == context && source.Graph.DependsOn(source.Root, changed))
            .OrderBy(source => source.Root, StringComparer.Ordinal).ToImmutableArray();
    }
    #endregion
}
