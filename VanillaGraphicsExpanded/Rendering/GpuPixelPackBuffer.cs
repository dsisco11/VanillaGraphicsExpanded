using System;
using VanillaGraphicsExpanded.Rendering.RHI;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an OpenGL PixelPackBuffer (PBO).
/// Intended for texture readback staging (GL_PIXEL_PACK_BUFFER).
/// </summary>
internal sealed class GpuPixelPackBuffer : GpuBufferObject
{
    /// <summary>Declares buffer policy before manager-owned native creation.</summary>
    internal GpuPixelPackBuffer(BufferUsage usage, string? debugName)
    {
        Role = BufferRole.PixelPack;
        allocationUsage = usage;
        this.debugName = debugName;
    }


    /// <summary>
    /// Allocates (or reallocates) the buffer store by orphaning the existing storage and creating a new store
    /// via <c>glBufferData(GL_PIXEL_PACK_BUFFER, byteCount, NULL, usage)</c>.
    /// </summary>
    /// <remarks>
    /// Requires a current GL context on the calling thread.
    /// This is useful for non-persistent readback paths that issue <c>glReadPixels</c> into the PBO and then map/unmap.
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

        GpuBufferUploader.Allocate(this, byteCount, IntPtr.Zero, allocationUsage);

    }


    /// <summary>
    /// Maps a subrange of the PBO into client address space using <c>glMapBufferRange</c>.
    /// </summary>
    /// <remarks>
    /// Requires a current GL context on the calling thread.
    /// Returns <see cref="IntPtr.Zero"/> on invalid buffers; GL failures are surfaced via OpenTK/driver behavior.
    /// The caller is responsible for calling <see cref="Unmap"/> when finished.
    /// </remarks>
    public IntPtr MapRange(int offsetBytes, int byteCount, BufferMapAccess access)
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


        return RHICommandContext.Current.MapBuffer(this, offsetBytes, byteCount, access);
    }

    /// <summary>
    /// Unmaps the PBO using <c>glUnmapBuffer(GL_PIXEL_PACK_BUFFER)</c>.
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


        return RHICommandContext.Current.UnmapBuffer(this);
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


        RHICommandContext.Current.FlushMappedBuffer(this, offsetBytes, byteCount);
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
        StorageAccess extraStorageFlags = 0)
    {
        if (!IsValid)
        {
            return IntPtr.Zero;
        }

        if (byteCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteCount), byteCount, "Byte count must be >= 0.");
        }


        StorageAccess storageFlags = StorageAccess.Read | StorageAccess.Persistent | extraStorageFlags;
        BufferMapAccess mapFlags = BufferMapAccess.Read | BufferMapAccess.Persistent;

        if (coherent)
        {
            storageFlags |= StorageAccess.Coherent;
            mapFlags |= BufferMapAccess.Coherent;
        }
        // Read-only mappings need no client flush; GPU completion governs visibility.

        RHICommandContext.Current.AllocateImmutableBuffer(this, byteCount, storageFlags);


        return RHICommandContext.Current.MapBuffer(this, 0, byteCount, mapFlags);
    }
}
