using System.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Packs the shared SI-unit wave snapshot consumed by color and liquid-depth programs.</summary>
internal sealed class LiquidWaveParamsUbo : CpuUniformBuffer
{
    internal const string BlockName = "VgeLiquidWaveParams";

    #region Frame inputs
    /// <summary>Allocates std140 phase and weather vectors.</summary>
    internal LiquidWaveParamsUbo() : base(32, Rendering.Uniforms.UniformBufferUsage.SingleFrame) { }

    /// <summary>Writes four world-anchored wave phases in radians.</summary>
    internal Vector4 Phases { set => WriteVector4(0, value); }

    /// <summary>Writes the dimensionless wind strength used to scale the waves.</summary>
    internal float Wind { set => WriteFloat(16, value); }

    #endregion
}
