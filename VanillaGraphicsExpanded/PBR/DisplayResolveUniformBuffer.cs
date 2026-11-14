using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>Retains the display handoff's two routing controls in one std140 block.</summary>
internal sealed class DisplayResolveUniformBuffer : CpuUniformBuffer
{
    #region Public API
    /// <summary>Byte extent of this concrete std140 layout.</summary>
    internal const int BlockSize = 16;
    /// <summary>Declares the immutable byte extent owned by this layout.</summary>
    public override int SizeBytes => BlockSize;

    /// <summary>Creates zero-default routing controls with single-frame publication.</summary>
    internal DisplayResolveUniformBuffer() : base() { }

    /// <summary>Selects scene-linear output without altering particle availability.</summary>
    internal int SceneLinear { set => WriteInt32(0, value); }

    /// <summary>Selects the completed particle layer without altering color convention.</summary>
    internal int ParticleLayerEnabled { set => WriteInt32(4, value); }
    #endregion
}
