using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Checks legacy description wire identities without constructing GPU resources.</summary>
public sealed class GraphicsCompatibilityDescriptionTests
{
    #region Public API
    /// <summary>Legacy partial masks retain their stable wire numbering and cannot be used as complete descriptions.</summary>
    [Fact]
    public void LegacyOverridesRemainASeparateStableContract()
    {
        // Arrange
        GlPipelineStateId[] ids = [GlPipelineStateId.DepthTestEnable, GlPipelineStateId.DepthFunc, GlPipelineStateId.DepthWriteMask,
            GlPipelineStateId.BlendEnable, GlPipelineStateId.BlendFunc, GlPipelineStateId.BlendEnableIndexed, GlPipelineStateId.BlendFuncIndexed,
            GlPipelineStateId.CullFaceEnable, GlPipelineStateId.ColorMask, GlPipelineStateId.ScissorTestEnable, GlPipelineStateId.LineWidth, GlPipelineStateId.PointSize];
        // Act and assert stable values stay in their legacy order and cannot enter complete descriptions.
        for (int index = 0; index < ids.Length; index++) Assert.Equal(index, (int)ids[index]);
        Assert.Equal(12, (int)GlPipelineStateId.Count);
        Assert.False(typeof(RHIGraphicsPipelineDescription).IsAssignableFrom(typeof(GlPipelineDesc)));
        Assert.DoesNotContain(typeof(RHIGraphicsPipelineDescription).GetConstructors(), constructor =>
            constructor.GetParameters().Any(parameter => parameter.ParameterType == typeof(GlPipelineDesc)));
    }
    #endregion
}
