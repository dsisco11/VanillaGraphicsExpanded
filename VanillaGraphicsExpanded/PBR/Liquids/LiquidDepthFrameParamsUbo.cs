using System;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Packs the liquid-depth projection matrix into its owned std140 block.</summary>
internal sealed class LiquidDepthFrameParamsUbo : CpuUniformBuffer
{
    internal const string BlockName = "VgeLiquidDepthFrameParams";

    #region Frame inputs
    /// <summary>Allocates one column-major projection matrix.</summary>
    internal LiquidDepthFrameParamsUbo() : base(64) { }

    /// <summary>Copies the engine's current projection matrix.</summary>
    internal ReadOnlySpan<float> ProjectionMatrix { set => WriteMatrix4(0, value); }
    #endregion
}
