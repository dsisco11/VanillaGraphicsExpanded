using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Executes buffer storage commands and forwards their native results to the resource manager.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Executes allocation on the currently bound buffer and publishes its supplied capacity and usage.</summary>
    internal void BufferData(GpuBufferObject buffer, int byteCount, IntPtr data, BufferUsageHint usage)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
        using var operation = new GpuResourceManager.MetadataOperation<GpuBufferMetadata>(buffer, GpuMetadataChange.Storage);
        GL.BufferData(buffer.Target, (IntPtr)byteCount, data, usage);
        operation.Commit(new GpuBufferMetadata(byteCount, usage));
    }

    /// <summary>Attempts native named allocation and publishes only after dispatch; publication failures propagate.</summary>
    internal bool TryNamedBufferData(GpuBufferObject buffer, int byteCount, IntPtr data, BufferUsageHint usage)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
        using var operation = new GpuResourceManager.MetadataOperation<GpuBufferMetadata>(buffer, GpuMetadataChange.Storage);
        // Only a native dispatch exception permits the uploader to retry through its bound path.
        // A publication failure must not trigger a second storage mutation.
        try { GL.NamedBufferData(buffer.BufferId, byteCount, data, usage); }
        catch { return false; }
        operation.Commit(new GpuBufferMetadata(byteCount, usage));
        return true;
    }

    /// <summary>Attempts named array allocation; only native dispatch failure permits the uploader's bound fallback.</summary>
    internal bool TryNamedBufferData<T>(GpuBufferObject buffer, int byteCount, T[] data, BufferUsageHint usage) where T : struct
    {
        ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
        using var operation = new GpuResourceManager.MetadataOperation<GpuBufferMetadata>(buffer, GpuMetadataChange.Storage);
        // Keep metadata publication outside the native exception handler to avoid a second allocation on publication failure.
        try { GL.NamedBufferData(buffer.BufferId, byteCount, data, usage); }
        catch { return false; }
        operation.Commit(new GpuBufferMetadata(byteCount, usage));
        return true;
    }

    /// <summary>Preserves the managed-array native marshaling path and publishes command-derived buffer storage.</summary>
    internal void BufferData<T>(GpuBufferObject buffer, int byteCount, T[] data, BufferUsageHint usage) where T : struct
    {
        ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
        using var operation = new GpuResourceManager.MetadataOperation<GpuBufferMetadata>(buffer, GpuMetadataChange.Storage);
        GL.BufferData(buffer.Target, byteCount, data, usage);
        operation.Commit(new GpuBufferMetadata(byteCount, usage));
    }

    /// <summary>Allocates immutable buffer storage and publishes its supplied capacity and guaranteed usage.</summary>
    internal void BufferStorage(GpuBufferObject buffer, int byteCount, IntPtr data, BufferStorageFlags flags)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
        using var operation = new GpuResourceManager.MetadataOperation<GpuBufferMetadata>(buffer, GpuMetadataChange.Storage);
        GL.BufferStorage(buffer.Target, byteCount, data, flags);
        operation.Commit(new GpuBufferMetadata(byteCount, BufferUsageHint.DynamicDraw));
    }
    #endregion
}
