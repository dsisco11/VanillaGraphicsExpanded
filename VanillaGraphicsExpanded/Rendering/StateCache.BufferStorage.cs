using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Executes buffer storage commands and forwards their native results to the resource manager.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Executes allocation on the currently bound buffer and publishes its actual capacity and usage.</summary>
    internal void BufferData(GpuBufferObject buffer, int byteCount, IntPtr data, BufferUsageHint usage)
    {
        RequireResourceOperationAvailable(buffer.Kind, buffer.ResourceId);
        GL.BufferData(buffer.Target, (IntPtr)byteCount, data, usage);
        GpuResourceManager.ObserveWrapperMetadata(buffer, new GpuBufferMetadataReader(), GpuMetadataChange.Storage);
    }

    /// <summary>Attempts native named allocation and publishes only after dispatch; publication failures propagate.</summary>
    internal bool TryNamedBufferData(GpuBufferObject buffer, int byteCount, IntPtr data, BufferUsageHint usage)
    {
        RequireResourceOperationAvailable(buffer.Kind, buffer.ResourceId);
        // Only a native dispatch exception permits the uploader to retry through its bound path.
        // A publication failure must not trigger a second storage mutation.
        try { GL.NamedBufferData(buffer.BufferId, byteCount, data, usage); }
        catch { return false; }
        GpuResourceManager.ObserveWrapperMetadata(buffer, new GpuBufferMetadataReader(), GpuMetadataChange.Storage);
        return true;
    }

    /// <summary>Attempts named array allocation; only native dispatch failure permits the uploader's bound fallback.</summary>
    internal bool TryNamedBufferData<T>(GpuBufferObject buffer, int byteCount, T[] data, BufferUsageHint usage) where T : struct
    {
        RequireResourceOperationAvailable(buffer.Kind, buffer.ResourceId);
        // Keep metadata publication outside the native exception handler to avoid a second allocation on publication failure.
        try { GL.NamedBufferData(buffer.BufferId, byteCount, data, usage); }
        catch { return false; }
        GpuResourceManager.ObserveWrapperMetadata(buffer, new GpuBufferMetadataReader(), GpuMetadataChange.Storage);
        return true;
    }

    /// <summary>Preserves the managed-array native marshaling path and publishes measured buffer storage.</summary>
    internal void BufferData<T>(GpuBufferObject buffer, int byteCount, T[] data, BufferUsageHint usage) where T : struct
    {
        RequireResourceOperationAvailable(buffer.Kind, buffer.ResourceId);
        GL.BufferData(buffer.Target, byteCount, data, usage);
        GpuResourceManager.ObserveWrapperMetadata(buffer, new GpuBufferMetadataReader(), GpuMetadataChange.Storage);
    }

    /// <summary>Allocates immutable buffer storage and publishes its actual capacity and native usage.</summary>
    internal void BufferStorage(GpuBufferObject buffer, int byteCount, IntPtr data, BufferStorageFlags flags)
    {
        RequireResourceOperationAvailable(buffer.Kind, buffer.ResourceId);
        GL.BufferStorage(buffer.Target, byteCount, data, flags);
        GpuResourceManager.ObserveWrapperMetadata(buffer, new GpuBufferMetadataReader(), GpuMetadataChange.Storage);
    }
    #endregion
}
