using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.Uniforms;

/// <summary>Owns one non-orphaned uniform store and its optional persistent mapping.</summary>
internal sealed class UniformStoragePage : IDisposable
{
    private readonly bool coherent;
    private readonly IntPtr mapped;
    internal GpuUniformBuffer Buffer { get; }
    internal int SlotSize { get; }
    internal int Capacity { get; }
    internal UniformStorageVersion?[] Slots { get; }
    internal long[] Generations { get; }

    #region Public API
    /// <summary>Allocates equal aligned slots with the selected upload mechanism.</summary>
    internal UniformStoragePage(int slotSize, int capacity, bool persistent, bool coherent)
    {
        SlotSize = slotSize;
        Capacity = capacity;
        this.coherent = coherent;
        Slots = new UniformStorageVersion?[capacity / slotSize];
        Generations = new long[Slots.Length];
        Buffer = GpuUniformBuffer.Create(debugName: "Uniform.PersistentPage");
        try
        {
            if (!persistent) Buffer.Allocate(capacity);
            else
            {
                using var binding = Buffer.BindScope();
                var storage = BufferStorageFlags.MapWriteBit | BufferStorageFlags.MapPersistentBit;
                var access = MapBufferAccessMask.MapWriteBit | MapBufferAccessMask.MapPersistentBit;
                if (coherent) { storage |= BufferStorageFlags.MapCoherentBit; access |= MapBufferAccessMask.MapCoherentBit; }
                else access |= MapBufferAccessMask.MapFlushExplicitBit;
                GL.BufferStorage(BufferTarget.UniformBuffer, capacity, IntPtr.Zero, storage);
                mapped = GL.MapBufferRange(BufferTarget.UniformBuffer, IntPtr.Zero, capacity, access);
                if (mapped == IntPtr.Zero) throw new InvalidOperationException("Persistent uniform mapping failed.");
            }
            if (GL.GetError() != ErrorCode.NoError) throw new InvalidOperationException("Uniform storage allocation failed.");
        }
        catch { Buffer.Dispose(); throw; }
    }

    /// <summary>Copies only into a reserved range that cannot overlap a submitted version.</summary>
    internal unsafe void Write(int offset, ReadOnlySpan<byte> data)
    {
        if (!Buffer.IsValid) throw new ObjectDisposedException(nameof(UniformStoragePage));
        if (mapped == IntPtr.Zero) Buffer.UploadSubData<byte>(data, offset, data.Length);
        else
        {
            fixed (byte* source = data)
                System.Buffer.MemoryCopy(source, (byte*)mapped + offset, data.Length, data.Length);
            if (!coherent)
            {
                using var binding = Buffer.BindScope();
                GL.FlushMappedBufferRange(BufferTarget.UniformBuffer, (IntPtr)offset, data.Length);
            }
        }
        if (GL.GetError() != ErrorCode.NoError) throw new InvalidOperationException("Uniform storage upload failed.");
    }

    /// <summary>Retires native storage through its established resource owner.</summary>
    public void Dispose() => Buffer.Dispose();
    #endregion
}
