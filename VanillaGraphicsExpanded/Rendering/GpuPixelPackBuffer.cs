using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
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
    internal GpuPixelPackBuffer(RHIBufferUsage usage, string? debugName)
    {
        target = BufferTarget.PixelPackBuffer;
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
    /// Issues a <c>glReadPixels</c> call that writes into this PBO at the given byte offset.
    /// </summary>
    /// <remarks>
    /// Requires a current GL context on the calling thread.
    /// The caller must ensure the desired read framebuffer is bound before calling this method.
    /// Rows use the requested alignment with no skips or byte swapping; prior pack state is restored.
    /// </remarks>
    public void ReadPixels(
        int x,
        int y,
        int width,
        int height,
        PixelFormat format,
        PixelType type,
        int dstOffsetBytes = 0,
        int packAlignment = 4)
    {
        if (!IsValid)
        {
            return;
        }

        if (dstOffsetBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dstOffsetBytes), dstOffsetBytes, "Offset must be >= 0.");
        }

        if (packAlignment is not (1 or 2 or 4 or 8))
        {
            throw new ArgumentOutOfRangeException(nameof(packAlignment), packAlignment, "PackAlignment must be 1, 2, 4, or 8.");
        }

        using var scope = BindScope();

        using var packScope = OpenGLPixelLayouts.PackScope(StateCache.Current, new StateCache.PixelPackState(packAlignment));
        GL.ReadPixels(x, y, width, height, format, type, (IntPtr)dstOffsetBytes);
    }
    /// <summary>
    /// Maps a subrange of the PBO into client address space using <c>glMapBufferRange</c>.
    /// </summary>
    /// <remarks>
    /// Requires a current GL context on the calling thread.
    /// Returns <see cref="IntPtr.Zero"/> on invalid buffers; GL failures are surfaced via OpenTK/driver behavior.
    /// The caller is responsible for calling <see cref="Unmap"/> when finished.
    /// </remarks>
    public IntPtr MapRange(int offsetBytes, int byteCount, RHIBufferMapAccess access)
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
        RHIStorageAccess extraStorageFlags = 0)
    {
        if (!IsValid)
        {
            return IntPtr.Zero;
        }

        if (byteCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteCount), byteCount, "Byte count must be >= 0.");
        }



        RHIStorageAccess storageFlags = RHIStorageAccess.Read | RHIStorageAccess.Persistent | extraStorageFlags;
        RHIBufferMapAccess mapFlags = RHIBufferMapAccess.Read | RHIBufferMapAccess.Persistent;

        if (coherent)
        {
            storageFlags |= RHIStorageAccess.Coherent;
            mapFlags |= RHIBufferMapAccess.Coherent;
        }
        // Read-only mappings need no client flush; GPU completion governs visibility.

        RHICommandContext.Current.AllocateImmutableBuffer(this, byteCount, storageFlags);


        return RHICommandContext.Current.MapBuffer(this, 0, byteCount, mapFlags);
    }
}
