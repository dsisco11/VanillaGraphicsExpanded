using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Protects hierarchy availability and the independent depth/PMJ resource contracts in generated cloud stages.</summary>
public sealed class CloudShaderBindingsTests
{
    #region Public API
    /// <summary>Only reduced tracing admits optional summaries, while reduction retains disjoint image slots and required native data.</summary>
    [Fact]
    public void HierarchyBindingsPreserveOptionalFallbackAndIndependentDepthInputs()
    {
        var stages = GeneratedShaderCatalog.Programs().SelectMany(program => program.Stages).DistinctBy(stage => stage.Identity).ToArray();
        var trace = stages.Single(stage => stage.Identity == "cloud_volume_trace.fsh").Bindings;
        Assert.Equal((8, false), (trace.Samplers["cloudHeightBounds"].Slot, trace.Samplers["cloudHeightBounds"].Required));
        Assert.Equal((15, true), (trace.UniformBlocks["CloudTraversalParameters"].Slot, trace.UniformBlocks["CloudTraversalParameters"].Required));
        Assert.Equal((14, true), (trace.UniformBlocks["CloudTemporalParameters"].Slot, trace.UniformBlocks["CloudTemporalParameters"].Required));
        foreach (var (name, slot) in new[] { ("cloudMap", 0), ("sceneDepth", 1), ("liquidDepth", 2), ("visibilityDepth", 6), ("cloudPmjJitter", 7) })
            Assert.Equal((slot, true), (trace.Samplers[name].Slot, trace.Samplers[name].Required));
        Assert.Equal((9, true), (trace.Samplers["cloudPrimaryNoise"].Slot, trace.Samplers["cloudPrimaryNoise"].Required));

        var reduction = stages.Single(stage => stage.Identity == "cloud_hierarchy.csh").Bindings;
        Assert.Equal((0, true), (reduction.Samplers["cloudMap"].Slot, reduction.Samplers["cloudMap"].Required));
        Assert.Equal((0, true), (reduction.Images["sourceBounds"].Slot, reduction.Images["sourceBounds"].Required));
        Assert.Equal((1, true), (reduction.Images["targetBounds"].Slot, reduction.Images["targetBounds"].Required));
        Assert.Equal((14, true), (reduction.UniformBlocks["CloudHierarchyParameters"].Slot, reduction.UniformBlocks["CloudHierarchyParameters"].Required));
        foreach (string name in new[] { "cloud_volume.fsh", "cloud_volume_temporal.fsh", "cloud_volume_resolve.fsh", "cloud_simple.fsh" })
        {
            var bindings = stages.Single(stage => stage.Identity == name).Bindings;
            Assert.False(bindings.Samplers.ContainsKey("cloudHeightBounds"));
            Assert.False(bindings.UniformBlocks.ContainsKey("CloudTraversalParameters"));
        }
        Assert.False(stages.Single(stage => stage.Identity == "cloud_volume_resolve.fsh").Bindings.Samplers.ContainsKey("cloudMap"));
        foreach (string name in new[] { "cloud_volume.fsh", "cloud_volume_temporal.fsh", "cloud_volume_resolve.fsh", "cloud_simple.fsh" })
            Assert.False(stages.Single(stage => stage.Identity == name).Bindings.Samplers.ContainsKey("cloudPrimaryNoise"));
        var temporal=stages.Single(stage=>stage.Identity=="cloud_volume_temporal.fsh").Bindings;
        Assert.Equal(2,temporal.Samplers.Count);
        Assert.Equal((0,true),(temporal.Samplers["currentPayload"].Slot,temporal.Samplers["currentPayload"].Required));
        Assert.Equal((7,true),(temporal.Samplers["historyPayload"].Slot,temporal.Samplers["historyPayload"].Required));
        var resolve=stages.Single(stage=>stage.Identity=="cloud_volume_resolve.fsh").Bindings;
        Assert.Contains("historyPayload",resolve.Samplers.Keys);
        Assert.DoesNotContain("history0",resolve.Samplers.Keys);
    }
    #endregion
}
