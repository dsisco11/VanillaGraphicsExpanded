namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Authorizes framebuffer discovery through the execution boundary without owning native calls.</summary>
internal sealed class GpuFramebufferMetadataReader : IGpuResourceMetadataReader<GpuFramebufferMetadata>
{
    internal static readonly GpuFramebufferMetadataReader Instance = new();

    #region Public API
    /// <inheritdoc />
    public bool IsResource(nint id) => RHI.RHICommandContext.Current.IsFramebufferResource(id);
    /// <inheritdoc />
    public bool TryRead(nint id, out GpuFramebufferMetadata metadata)
        => RHI.RHICommandContext.Current.ReadFramebufferMetadata(id, out metadata);
    #endregion
}
