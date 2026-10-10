using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using ShaderBuildTool.Spirv;
using TinyPreprocessor.Core;
using TinyPreprocessor.Graph;

namespace ShaderBuildTool.Tests;

/// <summary>Qualifies persistent library topology and actual-read source provenance.</summary>
public sealed class ShaderSourceRecordTests
{
    #region Public API
    /// <summary>Nested cross-domain imports retain canonical IDs, exact bytes and AST-free round trips.</summary>
    [Fact]
    public void NestedCrossDomainSourceRoundTripsWithActualReadHashes()
    {
        using var fixture = new ShaderBuildFixture();
        Directory.CreateDirectory(Path.Combine(fixture.Shaders, "includes"));
        Directory.CreateDirectory(Path.Combine(fixture.Assets, "other", "shaders"));
        File.WriteAllText(Path.Combine(fixture.Shaders, "root.fsh"), "@import \"./includes/outer.glsl\"\nvoid main() {}\n");
        File.WriteAllText(Path.Combine(fixture.Shaders, "includes", "outer.glsl"), "@import \"other:shaders/leaf.glsl\"\nfloat outerValue;\n");
        string leafPath = Path.Combine(fixture.Assets, "other", "shaders", "leaf.glsl");
        File.WriteAllText(leafPath, "float leafValue;\n", Encoding.Unicode);
        var source = new ShaderSourcePreprocessor(fixture.Assets, "vanillagraphicsexpanded").Expand("./root.fsh");
        Assert.Equal("vanillagraphicsexpanded:shaders/root.fsh", source.Root);
        Assert.Equal(3, source.Inputs.Length);
        var leaf = Assert.Single(source.Inputs, input => input.Path == leafPath);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(leafPath))), leaf.Hash);
        Assert.NotEqual(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(File.ReadAllText(leafPath)))), leaf.Hash);
        Assert.True(source.Graph.DependsOn(source.Root, [leaf.Resource]));
        var cache = new ShaderExpandedSourceCache(fixture.Output);
        cache.Store(source, "preprocessing");
        // Restoring from a fresh owner requires only persisted data and input hashing, with no parser API.
        Assert.True(new ShaderExpandedSourceCache(fixture.Output).TryRead(fixture.Assets, source.Root,
            "preprocessing", new ShaderFileHashIndex(fixture.Output, true), out var restored));
        Assert.Equal(source.Text, restored.Text);
        Assert.Equal(source.Graph.Resources.ToArray(), restored.Graph.Resources.ToArray());
        Assert.Equal(source.Graph.Edges.ToArray(), restored.Graph.Edges.ToArray());
        Assert.Equal(source.Graph.ProcessedResources.ToArray(), restored.Graph.ProcessedResources.ToArray());
        Assert.Equal(source.Inputs.ToArray(), restored.Inputs.ToArray());
        File.AppendAllText(leafPath, "float changed;\n", Encoding.Unicode);
        var misses = new List<string>();
        Assert.False(cache.TryRead(fixture.Assets, source.Root, "preprocessing", new ShaderFileHashIndex(fixture.Output, true), out _, misses.Add));
        Assert.Contains(misses, reason => reason.Contains("dependency changed"));
    }

    /// <summary>Replacing one root retains another root's shared edges and historical successful records.</summary>
    [Fact]
    public void ReplacingRootRemovesOnlyItsObsoleteEdges()
    {
        using var fixture = new ShaderBuildFixture();
        foreach (string name in new[] { "a", "b" })
            File.WriteAllText(Path.Combine(fixture.Shaders, name + ".fsh"), "@import \"fixture.inc\"\nvoid main() {}\n");
        var processor = new ShaderSourcePreprocessor(fixture.Assets, "vanillagraphicsexpanded");
        var cache = new ShaderExpandedSourceCache(fixture.Output);
        var a = processor.Expand("a.fsh");
        var b = processor.Expand("b.fsh");
        var planner = new ShaderDependencyPlanner();
        planner.Replace(a);
        planner.Replace(b);
        Assert.Equal(2, planner.Affected(fixture.Assets, ["vanillagraphicsexpanded:shaders/fixture.inc"]).Length);
        string historical = cache.Store(a, "preprocessing");
        cache.Store(b, "preprocessing");
        File.WriteAllText(Path.Combine(fixture.Shaders, "a.fsh"), "void main() {}\n");
        var isolated = processor.Expand("a.fsh");
        cache.Store(isolated, "preprocessing");
        planner.Replace(isolated);
        Assert.Equal(b.Root, Assert.Single(planner.Affected(fixture.Assets, ["vanillagraphicsexpanded:shaders/fixture.inc"])).Root);
        Assert.Empty(planner.Affected(Path.Combine(fixture.Root, "different-assets"), [b.Root]));
        planner.Remove(fixture.Assets, b.Root);
        Assert.Empty(planner.Affected(fixture.Assets, ["vanillagraphicsexpanded:shaders/fixture.inc"]));
        string include = "vanillagraphicsexpanded:shaders/fixture.inc";
        Assert.False(isolated.Graph.DependsOn(a.Root, [include]));
        Assert.True(b.Graph.DependsOn(b.Root, [include]));
        Assert.Single(isolated.Graph.Restore().GetAllResources());
        Assert.Empty(isolated.Graph.Restore().GetProcessingOrder());
        Assert.Single(isolated.Graph.ProcessedResources);
        Assert.True(File.Exists(Path.Combine(fixture.Output, "_cache", "sources", historical + ".json")));
        Assert.True(cache.TryRead(fixture.Assets, a.Root, "preprocessing", new ShaderFileHashIndex(fixture.Output, true), out var restored));
        Assert.Empty(restored.Graph.Edges);
        Assert.True(cache.TryRead(fixture.Assets, b.Root, "preprocessing", new ShaderFileHashIndex(fixture.Output, true), out var shared));
        Assert.True(shared.Graph.DependsOn(b.Root, [include]));
    }

    /// <summary>Failed preprocessing never replaces a successful head, whose changed inputs prevent false reuse.</summary>
    [Fact]
    public void FailedExpansionCannotPublishPartialGraph()
    {
        using var fixture = new ShaderBuildFixture();
        var processor = new ShaderSourcePreprocessor(fixture.Assets, "vanillagraphicsexpanded");
        var cache = new ShaderExpandedSourceCache(fixture.Output);
        var source = processor.Expand("fixture.fsh");
        cache.Store(source, "preprocessing");
        File.WriteAllText(Path.Combine(fixture.Shaders, "fixture.fsh"), "@import \"absent.glsl\"\nvoid main() {}\n");
        Assert.Throws<InvalidOperationException>(() => processor.Expand("fixture.fsh"));
        File.WriteAllText(Path.Combine(fixture.Shaders, "cycle.fsh"), "@import \"cycle.fsh\"\nvoid main() {}\n");
        Assert.Throws<InvalidOperationException>(() => processor.Expand("cycle.fsh"));
        Assert.Single(Directory.GetFiles(Path.Combine(fixture.Output, "_cache", "sources"), "*.json"));
        Assert.False(cache.TryRead(fixture.Assets, source.Root, "preprocessing", new ShaderFileHashIndex(fixture.Output, true), out _));
    }

    /// <summary>The restored library detects cycles while reusable source records reject them and missing associations.</summary>
    [Fact]
    public void GraphValidationRejectsIncompleteAndCyclicProvenance()
    {
        using var fixture = new ShaderBuildFixture();
        var source = new ShaderSourcePreprocessor(fixture.Assets, "vanillagraphicsexpanded").Expand("fixture.fsh");
        Assert.Throws<InvalidDataException>(() => ShaderExpandedSource.Validate(source with { Inputs = source.Inputs.RemoveAt(0) }));
        var cyclic = source.Graph with { Edges = source.Graph.Edges.Add(new(source.Graph.Edges[0].Dependency, source.Root)) };
        Assert.True(cyclic.Restore().HasCycles());
        Assert.True(cyclic.DependsOn(source.Root, [source.Graph.Edges[0].Dependency]));
        Assert.Throws<InvalidDataException>(() => ShaderExpandedSource.Validate(source with { Graph = cyclic }));
        Assert.Throws<InvalidDataException>(() => (source.Graph with { Edges = [new(source.Root, "unknown")] }).Restore());
        Assert.Throws<InvalidDataException>(() => (source.Graph with { ProcessedResources = [] }).Restore());
        Assert.Throws<InvalidDataException>(() => ShaderExpandedSource.Validate(source with
        { Inputs = source.Inputs.SetItem(0, source.Inputs[0] with { Path = Path.Combine(fixture.Root, "outside.glsl") }) }));
    }
    /// <summary>Missing inputs and incompatible source contexts cannot reuse successful records.</summary>
    [Fact]
    public void SourceIdentityAndMissingInputsMissLocally()
    {
        using var fixture = new ShaderBuildFixture();
        var source = new ShaderSourcePreprocessor(fixture.Assets, "vanillagraphicsexpanded").Expand("fixture.fsh");
        var cache = new ShaderExpandedSourceCache(fixture.Output);
        cache.Store(source, "preprocessing");
        Assert.False(cache.TryRead(fixture.Assets, source.Root, "changed", new ShaderFileHashIndex(fixture.Output), out _));
        Assert.False(cache.TryRead(Path.Combine(fixture.Root, "other"), source.Root, "preprocessing", new ShaderFileHashIndex(fixture.Output), out _));
        File.Delete(Path.Combine(fixture.Shaders, "fixture.inc"));
        Assert.False(cache.TryRead(fixture.Assets, source.Root, "preprocessing", new ShaderFileHashIndex(fixture.Output), out _));
    }

    /// <summary>Recomputed envelope checksums cannot authorize altered source provenance or escaped head references.</summary>
    [Theory]
    [InlineData("source-null")]
    [InlineData("graph-null")]
    [InlineData("inputs-default")]
    [InlineData("text")]
    [InlineData("head-path")]
    public void AlteredSourceRecordsMissLocally(string change)
    {
        using var fixture = new ShaderBuildFixture();
        var source = new ShaderSourcePreprocessor(fixture.Assets, "vanillagraphicsexpanded").Expand("fixture.fsh");
        var cache = new ShaderExpandedSourceCache(fixture.Output);
        string contentKey = cache.Store(source, "preprocessing");
        Assert.True(ShaderCacheKey.IsValid(contentKey));
        if (change == "head-path")
            new ShaderRecordStore(fixture.Output, "sourceHeads").Store(ShaderExpandedSourceCache.Key(fixture.Assets, source.Root, "preprocessing"),
                new { Version = 1, Record = "../outside" });
        else
        {
            var records = new ShaderRecordStore(fixture.Output, "sources");
            Assert.True(records.TryRead<ShaderExpandedSourceCache.Record>(contentKey, out var record));
            record = change switch
            {
                "source-null" => record with { Source = null! },
                "graph-null" => record with { Source = source with { Graph = null! } },
                "inputs-default" => record with { Source = source with { Inputs = [] } },
                _ => record with { Source = source with { Text = "tampered" } }
            };
            records.Store(contentKey, record);
        }
        var misses = new List<string>();
        Assert.False(cache.TryRead(fixture.Assets, source.Root, "preprocessing", new ShaderFileHashIndex(fixture.Output), out _, misses.Add));
        Assert.NotEmpty(misses);
    }

    #endregion
}
