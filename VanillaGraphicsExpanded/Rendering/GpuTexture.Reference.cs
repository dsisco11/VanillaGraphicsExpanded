namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Exposes owned and borrowed textures through the consumer-only contract.</summary>
public abstract partial class GpuTexture
{
    #region Public API
    /// <summary>Reports texture usability through the same manager contract used by consumer references.</summary>
    public new bool IsValid => RHIResourceManager.TryGetTextureReferenceMetadata(this, out _);
    /// <summary>Retains this wrapper without transferring lifecycle authority.</summary>
    public static implicit operator GpuTextureRef(GpuTexture? texture) => new(texture);
    #endregion
}
