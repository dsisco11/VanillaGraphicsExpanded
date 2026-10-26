using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Acquires unknown external buffer facts through the backend without owning native dispatch.</summary>
internal sealed class GpuBufferMetadataReader : IGpuResourceMetadataReader<GpuBufferMetadata>
{
    #region Public API
    /// <inheritdoc />
    public bool IsResource(nint id) => RHI.RHICommandContext.Current.OpenGL.IsExternalBuffer(id);

    /// <inheritdoc />
    public bool TryRead(nint id, out GpuBufferMetadata metadata) => RHI.RHICommandContext.Current.OpenGL.TryReadBufferMetadata(id, out metadata);
    #endregion
}
