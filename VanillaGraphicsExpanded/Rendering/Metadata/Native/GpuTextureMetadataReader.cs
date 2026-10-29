using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns explicit external texture acquisition while native reads belong to the admitted backend.</summary>
internal sealed class GpuTextureMetadataReader : IGpuResourceMetadataReader<GpuTextureMetadata>
{
    internal static readonly GpuTextureMetadataReader Instance = new();
    private readonly TextureDimension knownTarget;
    #region Internal API
    /// <summary>Retains an established target for face-specific external queries.</summary>
    internal GpuTextureMetadataReader(TextureDimension knownTarget = 0) => this.knownTarget = knownTarget;
    #endregion
    #region Public API
    /// <inheritdoc />
    public bool TryRead(nint id, out GpuTextureMetadata metadata) => RHI.RHICommandContext.Current.ReadExternalTexture(id, knownTarget, out metadata);
    #endregion
}
