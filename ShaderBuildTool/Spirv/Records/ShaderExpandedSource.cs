using System.Collections.Immutable;
using TinyPreprocessor.Core;

namespace ShaderBuildTool.Spirv;

/// <summary>Successful expanded text and immutable per-root dependency provenance, without live ASTs.</summary>
internal sealed record ShaderExpandedSource(string Root, string AssetsRoot, string Text, ShaderDependencySnapshot Graph, ImmutableArray<ShaderInputObservation> Inputs)
{
    #region Public API
    /// <summary>Rejects incomplete associations and uses the graph library to validate successful root provenance.</summary>
    internal static void Validate(ShaderExpandedSource source)
    {
        if (source is null || string.IsNullOrWhiteSpace(source.Root) || source.Text is null || source.Graph is null
            || source.Inputs.IsDefault || string.IsNullOrWhiteSpace(source.AssetsRoot)
            || !Path.IsPathFullyQualified(source.AssetsRoot) || Path.GetFullPath(source.AssetsRoot) != source.AssetsRoot)
            throw new InvalidDataException("Expanded source is incomplete.");
        var graph = source.Graph.Restore();
        if (!source.Graph.Resources.Contains(source.Root) || graph.HasCycles()) throw new InvalidDataException("Source graph lacks root or contains cycles.");
        var observed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var input in source.Inputs)
        {
            if (input is null || !observed.Add(input.Resource) || !ShaderRecordStore.IsDigest(input.Hash) || input.Length < 0
                || string.IsNullOrWhiteSpace(input.Path) || !Path.IsPathFullyQualified(input.Path)
                || Path.GetFullPath(input.Path) != input.Path
                || !input.Path.StartsWith(Path.TrimEndingDirectorySeparator(source.AssetsRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Source input association is missing, duplicated, or outside assets.");
        }
        if (!observed.SetEquals(source.Graph.Resources)) throw new InvalidDataException("Not every graph resource has exactly one input association.");
        // Reject unrelated nodes as well as missing nodes; every observation belongs to this root.
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<ResourceId>();
        pending.Push(new ResourceId(source.Root));
        while (pending.TryPop(out var id))
        {
            if (!reachable.Add(id.Path)) continue;
            foreach (var dependency in graph.GetDependencies(id)) pending.Push(dependency);
        }
        if (!reachable.SetEquals(source.Graph.Resources)) throw new InvalidDataException("Source graph includes unrelated resources.");
    }
    #endregion
}

