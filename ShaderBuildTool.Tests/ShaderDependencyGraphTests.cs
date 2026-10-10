using ShaderBuildTool.Spirv;
using TinyPreprocessor.Core;
using TinyPreprocessor.Graph;
using TinyTokenizer.Ast;
using VanillaGraphicsExpanded;

namespace ShaderBuildTool.Tests;

/// <summary>Verifies dependency discovery and query semantics of the installed shader preprocessing bridge.</summary>
public sealed class ShaderDependencyGraphTests
{
    #region Public API
    /// <summary>Nested imports expose direct edges, canonical resolver identities, and dependency-first ordering.</summary>
    [Fact]
    public void NestedImportsExposeDirectEdgesAndCanonicalResourceIds()
    {
        using var fixture = new ShaderBuildFixture();
        Directory.CreateDirectory(Path.Combine(fixture.Shaders, "includes"));
        File.WriteAllText(Path.Combine(fixture.Shaders, "includes", "outer.glsl"),
            "@import \"../includes/leaf.glsl\"\nfloat outerValue;\n");
        File.WriteAllText(Path.Combine(fixture.Shaders, "includes", "leaf.glsl"), "float leafValue;\n");
        var root = new ResourceId("vanillagraphicsexpanded:shaders/root.fsh");
        var outer = new ResourceId("vanillagraphicsexpanded:shaders/includes/outer.glsl");
        var leaf = new ResourceId("vanillagraphicsexpanded:shaders/includes/leaf.glsl");
        var reads = new Dictionary<ResourceId, string>();
        var resolver = new FileSystemSyntaxTreeResourceResolver(fixture.Assets, "vanillagraphicsexpanded",
            (id, path, text, tree) => reads[id] = path);
        var result = new ShaderSyntaxTreePreprocessor(resolver).Process(root,
            SyntaxTree.Parse("@import \"./includes/outer.glsl\"\nvoid main() {}\n", GlslSchema.Instance), ct: TestContext.Current.CancellationToken);

        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        var graph = result.DependencyGraph;
        Assert.NotNull(graph);
        Assert.Equal(new[] { outer }, graph.GetDependencies(root));
        Assert.Equal(new[] { leaf }, graph.GetDependencies(outer));
        Assert.Empty(graph.GetDependencies(leaf));
        Assert.Equal(new[] { outer }, graph.GetDependents(leaf));
        Assert.Equal(new[] { root }, graph.GetDependents(outer));
        Assert.Empty(graph.GetDependents(root));
        Assert.Equal(3, graph.GetAllResources().Count());
        Assert.Equal(new[] { leaf, outer, root }, graph.GetProcessingOrder());
        Assert.Equal(new[] { leaf, outer, root }, result.ProcessedResources);
        Assert.False(graph.HasCycles());
        Assert.Empty(graph.DetectCycles());
        // The root was supplied directly; only imported resources pass through the resolver callback.
        Assert.Equal(2, reads.Count);
        Assert.Equal(Path.Combine(fixture.Shaders, "includes", "outer.glsl"), reads[outer]);
        Assert.Equal(Path.Combine(fixture.Shaders, "includes", "leaf.glsl"), reads[leaf]);
        Assert.Contains("leafValue", result.Content.ToText());
    }

    /// <summary>An isolated root has graph membership even though the graph ordering omits it.</summary>
    [Fact]
    public void IsolatedRootRequiresMembershipBeyondGraphProcessingOrder()
    {
        using var fixture = new ShaderBuildFixture();
        var root = new ResourceId("vanillagraphicsexpanded:shaders/isolated.fsh");
        var result = new ShaderSyntaxTreePreprocessor(
            new FileSystemSyntaxTreeResourceResolver(fixture.Assets, "vanillagraphicsexpanded"))
            .Process(root, SyntaxTree.Parse("void main() {}\n", GlslSchema.Instance), ct: TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.NotNull(result.DependencyGraph);
        Assert.Equal(new[] { root }, result.DependencyGraph.GetAllResources());
        // The installed graph orders edges only; snapshots must enumerate all resources separately.
        Assert.Empty(result.DependencyGraph.GetProcessingOrder());
        Assert.Equal(new[] { root }, result.ProcessedResources);
        Assert.Empty(result.DependencyGraph.GetDependencies(root));
        Assert.Empty(result.DependencyGraph.GetDependents(root));
    }

    /// <summary>A cyclic import fails preprocessing and the graph library identifies a restored equivalent cycle.</summary>
    [Fact]
    public void CyclicImportsFailAndLibraryDetectsRestoredCycle()
    {
        using var fixture = new ShaderBuildFixture();
        const string source = "@import \"cycle.glsl\"\nvoid main() {}\n";
        File.WriteAllText(Path.Combine(fixture.Shaders, "root.fsh"), source);
        File.WriteAllText(Path.Combine(fixture.Shaders, "cycle.glsl"), "@import \"root.fsh\"\nfloat cycleValue;\n");
        var root = new ResourceId("vanillagraphicsexpanded:shaders/root.fsh");
        var include = new ResourceId("vanillagraphicsexpanded:shaders/cycle.glsl");
        var result = new ShaderSyntaxTreePreprocessor(
            new FileSystemSyntaxTreeResourceResolver(fixture.Assets, "vanillagraphicsexpanded"))
            .Process(root, SyntaxTree.Parse(source, GlslSchema.Instance), ct: TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => (diagnostic.ToString() ?? string.Empty).Contains("cycl", StringComparison.OrdinalIgnoreCase));
        // A failed preprocessing result must never qualify for persistence, even if it retains graph data.
        var graph = new ResourceDependencyGraph();
        graph.AddDependency(root, include);
        graph.AddDependency(include, root);
        Assert.True(graph.HasCycles());
        Assert.NotEmpty(graph.DetectCycles());
    }
    #endregion
}
