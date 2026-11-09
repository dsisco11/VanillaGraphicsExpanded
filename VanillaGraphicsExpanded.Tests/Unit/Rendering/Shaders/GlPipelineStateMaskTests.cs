using System;
using VanillaGraphicsExpanded.Rendering;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Shaders;

/// <summary>Checks stable pipeline-state mask layout and invalid state/value combinations.</summary>
public class GlPipelineStateMaskTests
{
    #region Public API
    #region Mask layout
    /// <summary>State identifiers retain their assigned bit indices and count.</summary>
    [Fact]
    public void GlPipelineStateId_BitIndices_AreStable()
    {
        // Arrange and Act
        // Assert: Enum ordinals are part of the packed mask layout.
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

    #endregion
    #region Validation failures
    /// <summary>A state cannot be selected in both default and nondefault masks.</summary>
    [Fact]
    public void GlPipelineStateValidation_Overlap_Throws()
    {
        // Arrange
        GlPipelineStateMask m = GlPipelineStateMask.From(GlPipelineStateId.DepthFunc);

        // Act and Assert
        Assert.Throws<ArgumentException>(() =>
            GlPipelineStateValidation.ValidateMaskBits(defaultMask: m, nonDefaultMask: m));
    }

    /// <summary>A selected nondefault state without a value is rejected.</summary>
    [Fact]
    public void GlPipelineStateValidation_MissingValues_Throws()
    {
        // Arrange
        GlPipelineStateMask nonDefault = GlPipelineStateMask.From(GlPipelineStateId.DepthFunc);
        var desc = new GlPipelineDesc(defaultMask: default, nonDefaultMask: nonDefault, validate: false);

        // Act and Assert
        Assert.Throws<ArgumentException>(() => GlPipelineStateValidation.ValidateDesc(desc));
    }

    /// <summary>Mask bits without a declared state identifier are rejected.</summary>
    [Fact]
    public void GlPipelineStateValidation_UnknownBits_Throws()
    {
        // Arrange
        GlPipelineStateMask unknown = new(1UL << 63);

        // Act and Assert
        Assert.Throws<ArgumentException>(() =>
            GlPipelineStateValidation.ValidateMaskBits(defaultMask: unknown, nonDefaultMask: default));
    }
    #endregion
    #endregion
}
