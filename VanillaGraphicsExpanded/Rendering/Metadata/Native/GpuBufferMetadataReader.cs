using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Captures actual buffer storage without binding the buffer or consuming the native error queue.</summary>
internal sealed class GpuBufferMetadataReader : IGpuResourceMetadataReader<GpuBufferMetadata>
{
    #region Public API
    /// <inheritdoc />
    public bool IsResource(nint id) => id != 0 && id >= int.MinValue && id <= uint.MaxValue && GL.IsBuffer(unchecked((int)id));

    /// <inheritdoc />
    public unsafe bool TryRead(nint id, out GpuBufferMetadata metadata)
    {
        metadata = default;
        if (!IsResource(id)) return false;
        // Invalid sentinels distinguish an unavailable query from legitimate zero-sized storage.
        long capacity = -1;
        int usage = -1;
        GL.GetNamedBufferParameter(unchecked((int)id), BufferParameterName.BufferSize, &capacity);
        GL.GetNamedBufferParameter(unchecked((int)id), BufferParameterName.BufferUsage, &usage);
        if (capacity < 0 || capacity > nint.MaxValue || !Enum.IsDefined((BufferUsageHint)usage)) return false;
        metadata = new((nint)capacity, (BufferUsageHint)usage);
        return true;
    }
    #endregion
}
