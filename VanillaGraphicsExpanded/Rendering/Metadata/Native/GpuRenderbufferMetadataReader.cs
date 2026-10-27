namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns explicit renderbuffer discovery while native query services remain backend-local.</summary>
internal sealed class GpuRenderbufferMetadataReader : IGpuResourceMetadataReader<GpuRenderbufferMetadata>
{
    internal static readonly GpuRenderbufferMetadataReader Instance = new();
    #region Public API
    /// <inheritdoc />
    public bool IsResource(nint id) => RHI.RHICommandContext.Current.IsExternalRenderbuffer(id);
    /// <inheritdoc />
    public bool TryRead(nint id, out GpuRenderbufferMetadata metadata) => RHI.RHICommandContext.Current.ReadExternalRenderbuffer(id, out metadata);
    #endregion
}
