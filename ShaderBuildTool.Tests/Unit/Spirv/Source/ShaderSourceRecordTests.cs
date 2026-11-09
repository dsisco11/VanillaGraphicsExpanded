using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using ShaderBuildTool.Spirv;
using TinyPreprocessor.Core;
using TinyPreprocessor.Graph;
using ShaderBuildTool.Tests.Support.Generation;

namespace ShaderBuildTool.Tests.Unit.Spirv.Source;

/// <summary>Checks validation rules for source graph provenance, membership, and cycles.</summary>
public sealed class ShaderSourceRecordTests
{
    #region Public API

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

    #endregion
}
