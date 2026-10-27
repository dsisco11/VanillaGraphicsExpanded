using VanillaGraphicsExpanded.Rendering.RHI;
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
        Buffer = RHICommandContext.Current.CreateUniformBuffer(new RHIBufferDescription { Role = BufferRole.Uniform, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = "Uniform.PersistentPage" }).RequireResource();
        try
        {
            if (!persistent) Buffer.Allocate(capacity);
            else
            {

                var storage = StorageAccess.Write | StorageAccess.Persistent;
                var access = BufferMapAccess.Write | BufferMapAccess.Persistent;
                if (coherent) { storage |= StorageAccess.Coherent; access |= BufferMapAccess.Coherent; }
                else access |= BufferMapAccess.ExplicitFlush;
                RHICommandContext.Current.AllocateImmutableBuffer(Buffer, capacity, storage);
                mapped = RHICommandContext.Current.MapBuffer(Buffer, 0, capacity, access);
                if (mapped == IntPtr.Zero) throw new InvalidOperationException("Persistent uniform mapping failed.");
            }

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

                RHICommandContext.Current.FlushMappedBuffer(Buffer, offset, data.Length);
            }
        }

    }

    /// <summary>Retires native storage through its established resource owner.</summary>
    public void Dispose() => Buffer.Dispose();
    #endregion
}
