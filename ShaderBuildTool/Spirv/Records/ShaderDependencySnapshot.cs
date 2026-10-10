using System.Collections.Immutable;
using TinyPreprocessor.Core;
using TinyPreprocessor.Graph;

namespace ShaderBuildTool.Spirv;

/// <summary>Persists one library-discovered relationship without interpreting its opaque identities.</summary>
internal sealed record ShaderDependencyEdge(string Dependent, string Dependency);

/// <summary>Immutable topology and processing membership from one successful preprocessing operation.</summary>
internal sealed record ShaderDependencySnapshot(ImmutableArray<string> Resources, ImmutableArray<ShaderDependencyEdge> Edges, ImmutableArray<string> ProcessedResources)
{
    #region Public API
    /// <summary>Captures graph membership separately from ordering so isolated roots survive persistence.</summary>
    internal static ShaderDependencySnapshot Capture(ResourceDependencyGraph graph, IEnumerable<ResourceId> processed)
    {
        var resources = graph.GetAllResources().OrderBy(id => id.Path, StringComparer.Ordinal).ToArray();
        return new(resources.Select(id => id.Path).ToImmutableArray(), resources.SelectMany(id => graph.GetDependencies(id)
            .OrderBy(dependency => dependency.Path, StringComparer.Ordinal).Select(dependency => new ShaderDependencyEdge(id.Path, dependency.Path))).ToImmutableArray(),
            processed.Select(id => id.Path).ToImmutableArray());
    }

    /// <summary>Restores nodes before relationships; the caller exclusively owns the resulting mutable graph.</summary>
    internal ResourceDependencyGraph Restore()
    {
        if (Resources.IsDefault || Edges.IsDefault || ProcessedResources.IsDefault || Resources.Any(string.IsNullOrWhiteSpace)
            || Resources.Distinct(StringComparer.Ordinal).Count() != Resources.Length
            || ProcessedResources.Distinct(StringComparer.Ordinal).Count() != ProcessedResources.Length
            || !Resources.ToHashSet(StringComparer.Ordinal).SetEquals(ProcessedResources))
            throw new InvalidDataException("Dependency membership is incomplete or duplicated.");
        var nodes = Resources.ToHashSet(StringComparer.Ordinal);
        var graph = new ResourceDependencyGraph();
        foreach (string id in Resources) graph.AddResource(new ResourceId(id));
        foreach (var edge in Edges)
        {
            if (edge is null || !nodes.Contains(edge.Dependent) || !nodes.Contains(edge.Dependency))
                throw new InvalidDataException("Dependency edge has an unknown endpoint.");
            graph.AddDependency(new ResourceId(edge.Dependent), new ResourceId(edge.Dependency));
        }
        if (Edges.Distinct().Count() != Edges.Length) throw new InvalidDataException("Duplicate dependency edge.");
        return graph;
    }

    /// <summary>Uses library queries and a visited set because the installed API returns direct neighbors.</summary>
    internal bool DependsOn(string root, IEnumerable<string> changedResources)
    {
        var graph = Restore();
        var pending = new Stack<ResourceId>(changedResources.Select(id => new ResourceId(id)));
        var visited = new HashSet<ResourceId>();
        while (pending.TryPop(out var id))
        {
            if (!visited.Add(id)) continue;
            if (id.Path == root) return true;
            foreach (var parent in graph.GetDependents(id)) pending.Push(parent);
        }
        return false;
    }
    #endregion
}
