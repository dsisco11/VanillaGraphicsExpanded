using System;
using VanillaGraphicsExpanded.Rendering;
using Xunit;

namespace VanillaGraphicsExpanded.Tests;

public class GlPipelineStateMaskTests
{
    [Fact]
    public void GlPipelineStateId_BitIndices_AreStable()
    {
        Assert.Equal(0, (int)GlPipelineStateId.DepthTestEnable);
        Assert.Equal(1, (int)GlPipelineStateId.DepthFunc);
        Assert.Equal(2, (int)GlPipelineStateId.DepthWriteMask);
        Assert.Equal(3, (int)GlPipelineStateId.BlendEnable);
        Assert.Equal(4, (int)GlPipelineStateId.BlendFunc);
        Assert.Equal(5, (int)GlPipelineStateId.BlendEnableIndexed);
        Assert.Equal(6, (int)GlPipelineStateId.BlendFuncIndexed);
        Assert.Equal(7, (int)GlPipelineStateId.CullFaceEnable);
        Assert.Equal(8, (int)GlPipelineStateId.ColorMask);
        Assert.Equal(9, (int)GlPipelineStateId.ScissorTestEnable);
        Assert.Equal(10, (int)GlPipelineStateId.LineWidth);
        Assert.Equal(11, (int)GlPipelineStateId.PointSize);
        Assert.Equal(12, (int)GlPipelineStateId.Count);
    }

    [Fact]
    public void GlPipelineStateValidation_Overlap_Throws()
    {
        GlPipelineStateMask m = GlPipelineStateMask.From(GlPipelineStateId.DepthFunc);

        Assert.Throws<ArgumentException>(() =>
            GlPipelineStateValidation.ValidateMaskBits(defaultMask: m, nonDefaultMask: m));
    }

    [Fact]
    public void GlPipelineStateValidation_MissingValues_Throws()
    {
        GlPipelineStateMask nonDefault = GlPipelineStateMask.From(GlPipelineStateId.DepthFunc);
        var desc = new GlPipelineDesc(defaultMask: default, nonDefaultMask: nonDefault, validate: false);

        Assert.Throws<ArgumentException>(() => GlPipelineStateValidation.ValidateDesc(desc));
    }

    [Fact]
    public void GlPipelineStateValidation_UnknownBits_Throws()
    {
        GlPipelineStateMask unknown = new(1UL << 63);

        Assert.Throws<ArgumentException>(() =>
            GlPipelineStateValidation.ValidateMaskBits(defaultMask: unknown, nonDefaultMask: default));
    }
}
