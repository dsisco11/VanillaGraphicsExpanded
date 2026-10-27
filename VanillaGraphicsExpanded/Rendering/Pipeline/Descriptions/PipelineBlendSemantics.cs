using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

/// <summary>Defines blend operand dependencies shared by description validation and state relevance.</summary>
internal static class PipelineBlendSemantics
{
    #region Internal API
    /// <summary>Identifies equations whose results depend on the authored source and destination factors.</summary>
    internal static bool UsesFactors(BlendOperation equation) => equation is not (BlendOperation.Minimum or BlendOperation.Maximum);
    /// <summary>Identifies multipliers that consume the declared dynamic blend constant.</summary>
    internal static bool UsesConstant(BlendFactor factor) => factor is BlendFactor.ConstantColor
        or BlendFactor.OneMinusConstantColor or BlendFactor.ConstantAlpha or BlendFactor.OneMinusConstantAlpha;
    #endregion
}
