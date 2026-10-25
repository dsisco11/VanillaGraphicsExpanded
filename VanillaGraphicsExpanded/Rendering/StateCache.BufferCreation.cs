using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Realizes buffer objects and establishes initial storage without descriptor rediscovery.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Creates a private native buffer and returns command-derived facts for manager handoff.</summary>
    internal GpuBufferMetadata? CreateBuffer(in GpuBufferInitialization initialization, out nint id)
    {
        id = 0;
        bool named = GpuSupport.Graphics.SupportsArbDirectStateAccess;
        if (named)
        {
            try { GL.CreateBuffers(1, out int created); id = created; }
            catch { named = false; }
            if (id == 0) named = false;
        }
        if (!named) id = GL.GenBuffer();
        if (id == 0) return null;

        // A generated name is not yet an object. Realize it through a neutral binding
        // target, preserving both the caller's binding and its VAO element association.
        using var binding = named ? (BufferScope?)null : BindBufferScope(BufferTarget.CopyWriteBuffer, (int)id);
        if (initialization.Mode == GpuBufferStorageMode.Realization)
            return new GpuBufferMetadata(0, BufferUsageHint.StaticDraw);
        if (initialization.Mode == GpuBufferStorageMode.Mutable)
        {
            if (named) GL.NamedBufferData((int)id, initialization.Capacity, IntPtr.Zero, initialization.Usage);
            else GL.BufferData(BufferTarget.CopyWriteBuffer, initialization.Capacity, IntPtr.Zero, initialization.Usage);
            return new GpuBufferMetadata(initialization.Capacity, initialization.Usage);
        }
        if (named) GL.NamedBufferStorage((int)id, initialization.Capacity, IntPtr.Zero, initialization.StorageFlags);
        else GL.BufferStorage(BufferTarget.CopyWriteBuffer, initialization.Capacity, IntPtr.Zero, initialization.StorageFlags);
        return new GpuBufferMetadata(initialization.Capacity, BufferUsageHint.DynamicDraw);
    }
    #endregion
}
