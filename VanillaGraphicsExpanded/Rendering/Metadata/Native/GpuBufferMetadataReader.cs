using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Acquires unknown external buffer facts through the backend without owning native dispatch.</summary>
internal sealed class GpuBufferMetadataReader : IGpuResourceMetadataReader<GpuBufferMetadata>
{
    #region Public API

    /// <inheritdoc />
    public bool TryRead(nint id, out GpuBufferMetadata metadata) => RHI.RHICommandContext.Current.TryReadBufferMetadata(id, out metadata);
    #endregion
}
