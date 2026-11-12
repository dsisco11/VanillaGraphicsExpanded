using System.Collections.Immutable;
using TinyPreprocessor.Core;

namespace ShaderBuildTool.Spirv;

/// <summary>Successful expanded text and immutable per-root dependency provenance, without live ASTs.</summary>
internal sealed record ShaderExpandedSource(string Root, string AssetsRoot, string Text, ShaderDependencySnapshot Graph, ImmutableArray<ShaderInputObservation> Inputs)
{
    #region Public API
    /// <summary>Retains exact observed inputs and graph edges when a generated stage consumes another expanded stage's interface.</summary>
    internal ShaderExpandedSource Derive(string text, ShaderExpandedSource dependency)
    {
        Validate(this); Validate(dependency);
        if (AssetsRoot != dependency.AssetsRoot || Root == dependency.Root)
            throw new InvalidDataException("Derived stage dependencies must share an asset tree and have distinct roots.");
        var graph = Graph.Restore();
        foreach (string resource in dependency.Graph.Resources) graph.AddResource(new ResourceId(resource));
        foreach (var edge in dependency.Graph.Edges)
            graph.AddDependency(new ResourceId(edge.Dependent), new ResourceId(edge.Dependency));
        graph.AddDependency(new ResourceId(Root), new ResourceId(dependency.Root));
        var observations = Inputs.ToDictionary(input => input.Resource, StringComparer.Ordinal);
        foreach (var input in dependency.Inputs)
        {
            if (observations.TryGetValue(input.Resource, out var previous) && previous != input)
                throw new IOException("Shader input changed while deriving a stage: " + input.Resource);
            observations[input.Resource] = input;
        }
        var result = this with
        {
            Text = text,
            Graph = ShaderDependencySnapshot.Capture(graph, graph.GetAllResources()),
            Inputs = observations.Values.OrderBy(input => input.Resource, StringComparer.Ordinal).ToImmutableArray()
        };
        Validate(result);
        return result;
    }

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

