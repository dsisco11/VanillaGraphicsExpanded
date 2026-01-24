using System;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an OpenGL PixelUnpackBuffer (PBO).
/// Intended for texture upload staging (GL_PIXEL_UNPACK_BUFFER).
/// </summary>
internal sealed class GpuPixelUnpackBuffer : GpuBufferObject
{
    private GpuPixelUnpackBuffer(int bufferId, BufferUsageHint usage, string? debugName)
    {
        this.bufferId = bufferId;
        target = BufferTarget.PixelUnpackBuffer;
        this.usage = usage;
        this.debugName = debugName;
    }

    /// <summary>
    /// Creates a new PixelUnpackBuffer (PBO) object name and wraps it in an RAII instance.
    /// </summary>
    /// <remarks>
    /// Requires a current GL context on the calling thread.
    /// The returned buffer starts with <see cref="GpuBufferObject.SizeBytes"/> = 0 and no allocated storage.
    /// </remarks>
    public static GpuPixelUnpackBuffer Create(
        BufferUsageHint usage = BufferUsageHint.StreamDraw,
        string? debugName = null)
    {
        int id = CreateBufferId(debugName);
        return new GpuPixelUnpackBuffer(id, usage, debugName);
    }

    /// <summary>
    /// Allocates (or reallocates) the buffer store by orphaning the existing storage and creating a new store
    /// via <c>glBufferData(GL_PIXEL_UNPACK_BUFFER, byteCount, NULL, usage)</c>.
    /// </summary>
    /// <remarks>
    /// Requires a current GL context on the calling thread.
    /// This is useful for non-persistent PBO upload paths that map, write, and unmap per upload.
    /// </remarks>
    public void AllocateOrphan(int byteCount)
    {
        if (!IsValid)
        {
            return;
        }

        if (byteCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteCount), byteCount, "Byte count must be >= 0.");
        }

        using var scope = BindScope();
        GL.BufferData(BufferTarget.PixelUnpackBuffer, (IntPtr)byteCount, IntPtr.Zero, usage);
        sizeBytes = byteCount;
    }

    /// <summary>
    /// Maps a subrange of the PBO into client address space using <c>glMapBufferRange</c>.
    /// </summary>
    /// <remarks>
    /// Requires a current GL context on the calling thread.
    /// Returns <see cref="IntPtr.Zero"/> on invalid buffers; GL failures are surfaced via OpenTK/driver behavior.
    /// The caller is responsible for calling <see cref="Unmap"/> when finished.
    /// </remarks>
    public IntPtr MapRange(int offsetBytes, int byteCount, MapBufferAccessMask access)
    {
        if (!IsValid)
        {
            return IntPtr.Zero;
        }

        if (offsetBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offsetBytes), offsetBytes, "Offset must be >= 0.");
        }

        if (byteCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteCount), byteCount, "Byte count must be >= 0.");
        }

        using var scope = BindScope();
        return GL.MapBufferRange(BufferTarget.PixelUnpackBuffer, (IntPtr)offsetBytes, (IntPtr)byteCount, access);
    }

    /// <summary>
    /// Unmaps the PBO using <c>glUnmapBuffer(GL_PIXEL_UNPACK_BUFFER)</c>.
    /// </summary>
    /// <remarks>
    /// Requires a current GL context on the calling thread.
    /// Returns false if the buffer is invalid or if the driver reports the data store became corrupt.
    /// </remarks>
    public bool Unmap()
    {
        if (!IsValid)
        {
            return false;
        }

        using var scope = BindScope();
        return GL.UnmapBuffer(BufferTarget.PixelUnpackBuffer);
    }

    /// <summary>
    /// Flushes an explicit mapped range using <c>glFlushMappedBufferRange</c>.
    /// </summary>
    /// <remarks>
    /// Requires a current GL context on the calling thread.
    /// Only needed for mappings created without coherent mapping and with explicit flush semantics.
    /// </remarks>
    public void FlushMappedRange(int offsetBytes, int byteCount)
    {
        if (!IsValid)
        {
            return;
        }

        if (offsetBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offsetBytes), offsetBytes, "Offset must be >= 0.");
        }

        if (byteCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteCount), byteCount, "Byte count must be >= 0.");
        }

        using var scope = BindScope();
        GL.FlushMappedBufferRange(BufferTarget.PixelUnpackBuffer, (IntPtr)offsetBytes, byteCount);
    }

    /// <summary>
    /// Allocates persistent mapped storage via <c>glBufferStorage</c> and maps it via <c>glMapBufferRange</c>.
    /// </summary>
    /// <param name="byteCount">Total buffer size in bytes.</param>
    /// <param name="coherent">If true, requests coherent mapping; otherwise enables explicit flush semantics.</param>
    /// <param name="extraStorageFlags">Optional extra storage flags to OR into the storage flags.</param>
    /// <returns>A pointer to the mapped base address, or <see cref="IntPtr.Zero"/> if the buffer is invalid.</returns>
    /// <remarks>
    /// Requires a current GL context on the calling thread.
    /// This method does not call <see cref="Unmap"/>; the caller should unmap during teardown if desired.
    /// </remarks>
    public IntPtr AllocateAndMapPersistent(
        int byteCount,
        bool coherent,
        BufferStorageFlags extraStorageFlags = 0)
    {
        if (!IsValid)
        {
            return IntPtr.Zero;
        }

        if (byteCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteCount), byteCount, "Byte count must be >= 0.");
        }

        using var scope = BindScope();

        BufferStorageFlags storageFlags = BufferStorageFlags.MapWriteBit | BufferStorageFlags.MapPersistentBit | extraStorageFlags;
        MapBufferAccessMask mapFlags = MapBufferAccessMask.MapWriteBit | MapBufferAccessMask.MapPersistentBit;

        if (coherent)
        {
            storageFlags |= BufferStorageFlags.MapCoherentBit;
            mapFlags |= MapBufferAccessMask.MapCoherentBit;
        }
        else
        {
            mapFlags |= MapBufferAccessMask.MapFlushExplicitBit;
        }

        GL.BufferStorage(BufferTarget.PixelUnpackBuffer, byteCount, IntPtr.Zero, storageFlags);
        sizeBytes = byteCount;

        return GL.MapBufferRange(BufferTarget.PixelUnpackBuffer, IntPtr.Zero, byteCount, mapFlags);
    }
}
