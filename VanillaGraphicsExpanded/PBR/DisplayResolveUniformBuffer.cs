using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>Retains the display handoff's two routing controls in one std140 block.</summary>
internal sealed class DisplayResolveUniformBuffer : CpuUniformBuffer
{
    #region Public API
    /// <summary>Creates zero-default routing controls with single-frame publication.</summary>
    internal DisplayResolveUniformBuffer() : base(16) { }

    /// <summary>Selects scene-linear output without altering particle availability.</summary>
    internal int SceneLinear { set => WriteInt32(0, value); }

    /// <summary>Selects the completed particle layer without altering color convention.</summary>
    internal int ParticleLayerEnabled { set => WriteInt32(4, value); }
    #endregion
}
