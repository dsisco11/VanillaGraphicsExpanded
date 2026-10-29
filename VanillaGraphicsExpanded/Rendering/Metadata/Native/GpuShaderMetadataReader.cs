using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Acquires the immutable native stage of an adopted shader object.</summary>
internal sealed class GpuShaderMetadataReader : IGpuResourceMetadataReader<GpuShaderMetadata>
{
    internal static readonly GpuShaderMetadataReader Instance = new();

    #region Public API

    /// <inheritdoc />
    public bool TryRead(nint id, out GpuShaderMetadata metadata) => RHICommandContext.Current.ReadShaderMetadata(id, out metadata);
    #endregion
}
