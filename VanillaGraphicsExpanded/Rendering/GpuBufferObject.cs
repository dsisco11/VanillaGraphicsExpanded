using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Base class for OpenGL buffer object wrappers.
/// All methods require a current GL context on the calling thread.
/// </summary>
public abstract class GpuBufferObject : GpuResource<GpuBufferMetadata>, IDisposable
{
    protected int bufferId;
    /// <summary>Reads the canonical native capacity through the typed snapshot.</summary>
    protected int sizeBytes => checked((int)metadata.Capacity);
    protected BufferTarget target;
    /// <summary>Reads the canonical storage usage through the typed snapshot.</summary>
    protected BufferUsageHint usage => metadata.Usage;
    /// <summary>Contains the requested hint for the next allocation, independently of observed storage.</summary>
    protected RHIBufferUsage allocationUsage;
    protected string? debugName;

    private readonly ConcurrentQueue<PendingUpload> pendingUploads = new();
    private int uploadsQueued;

    /// <summary>Supplies a non-owning consumer value without acquisition or native work.</summary>
    public static implicit operator GpuBufferRef(GpuBufferObject? buffer) => new(buffer);
    /// <summary>Gets the intended semantic role retained by this owning abstraction.</summary>
    public RHIBufferRole Role => RHI.Backends.OpenGL.OpenGLBufferValues.Role(target);
    public int BufferId => bufferId;
    public int SizeBytes => sizeBytes;
    public BufferTarget Target => target;
    public BufferUsageHint Usage => usage;
    public string? DebugName => debugName;

    /// <inheritdoc />
    public override nint ResourceId
    {
        get => bufferId;
        protected set => bufferId = (int)value;
    }

    protected override GpuResourceKind ResourceKind => GpuResourceKind.Buffer;


    public override void SetDebugName(string? debugName)
    {
        this.debugName = debugName;

#if DEBUG
        if (bufferId != 0)
        {
            GlDebug.TryLabel(ObjectLabelIdentifier.Buffer, bufferId, debugName);
        }
#endif
    }

    /// <summary>Binds this live allocation and restores the previous binding when the scope ends.</summary>
    public BindingScope BindScope()
    {
        ObjectDisposedException.ThrowIf(!IsValid, this);
        GpuResourceManager.RequireBufferReferenceMetadata(this);
        return new BindingScope(StateCache.Current.BindBufferScope(target, bufferId));
    }

    public void Bind()
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuBufferObject] Attempted to bind disposed or invalid buffer");
            return;
        }

        RHICommandContext.Current.BindBuffer(Role, this);
    }

    public bool TryBind()
    {
        if (!IsValid)
        {
            return false;
        }

        RHICommandContext.Current.BindBuffer(Role, this);
        return true;
    }

    public void Unbind()
    {
        RHICommandContext.Current.BindBuffer(Role, default);
    }

    public void Allocate(int sizeBytes)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuBufferObject] Attempted to allocate disposed or invalid buffer");
            return;
        }

        if (sizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), sizeBytes, "Size must be >= 0.");
        }

        GpuBufferUploader.Allocate(this, sizeBytes, IntPtr.Zero, allocationUsage);

    }

    public bool TryAllocate(int sizeBytes)
    {
        if (!IsValid || sizeBytes < 0)
        {
            return false;
        }

        try
        {
            Allocate(sizeBytes);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void EnsureCapacity(int minSizeBytes, bool growExponentially = true)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuBufferObject] Attempted to EnsureCapacity on disposed or invalid buffer");
            return;
        }

        if (minSizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minSizeBytes), minSizeBytes, "Size must be >= 0.");
        }

        if (sizeBytes >= minSizeBytes)
        {
            return;
        }

        int newCapacity = minSizeBytes;
        if (growExponentially && sizeBytes > 0)
        {
            newCapacity = Math.Max(newCapacity, checked(sizeBytes * 2));
        }

        GpuBufferUploader.Allocate(this, newCapacity, IntPtr.Zero, allocationUsage);

    }

    public bool TryEnsureCapacity(int minSizeBytes, bool growExponentially = true)
    {
        if (!IsValid || minSizeBytes < 0)
        {
            return false;
        }

        try
        {
            EnsureCapacity(minSizeBytes, growExponentially);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void UploadData<T>(T[] data) where T : struct
    {
        ArgumentNullException.ThrowIfNull(data);
        UploadData(data, checked(data.Length * Marshal.SizeOf<T>()));
    }

    public bool TryUploadData<T>(T[] data) where T : struct
    {
        if (!IsValid || data is null)
        {
            return false;
        }

        try
        {
            UploadData(data);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public unsafe void UploadData<T>(ReadOnlySpan<T> data) where T : unmanaged
    {
        UploadData(data, checked(data.Length * sizeof(T)));
    }

    public unsafe bool TryUploadData<T>(ReadOnlySpan<T> data) where T : unmanaged
    {
        if (!IsValid)
        {
            return false;
        }

        try
        {
            UploadData(data);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void UploadData<T>(T[] data, int byteCount) where T : struct
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuBufferObject] Attempted to upload data to disposed or invalid buffer");
            return;
        }

        ArgumentNullException.ThrowIfNull(data);

        if (byteCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteCount), byteCount, "Byte count must be >= 0.");
        }

        int maxByteCount = checked(data.Length * Marshal.SizeOf<T>());
        if (byteCount > maxByteCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(byteCount),
                byteCount,
                $"Byte count exceeds source array size ({maxByteCount} bytes for {data.Length}×{typeof(T).Name}).");
        }

        if (GpuResourceManagerSystem.IsInitialized
            && GpuResourceManagerSystem.IsRenderThreadKnown
            && !GpuResourceManagerSystem.IsRenderThread)
        {
            EnqueueUpload(PendingUpload.ForBufferData(CopyToPooledBytes(data, byteCount), byteCount));
            return;
        }

        if (TryGetBlittableArrayPointer(data, out GCHandle handle, out IntPtr ptr))
        {
            try
            {
                GpuBufferUploader.Allocate(this, byteCount, ptr, allocationUsage);

                return;
            }
            finally
            {
                if (handle.IsAllocated)
                {
                    handle.Free();
                }
            }
        }

        GpuBufferUploader.Allocate(this, byteCount, data, allocationUsage);

    }

    public bool TryUploadData<T>(T[] data, int byteCount) where T : struct
    {
        if (!IsValid || data is null)
        {
            return false;
        }

        try
        {
            UploadData(data, byteCount);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public unsafe void UploadData<T>(ReadOnlySpan<T> data, int byteCount) where T : unmanaged
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuBufferObject] Attempted to upload data to disposed or invalid buffer");
            return;
        }

        if (byteCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteCount), byteCount, "Byte count must be >= 0.");
        }

        int maxByteCount = checked(data.Length * sizeof(T));
        if (byteCount > maxByteCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(byteCount),
                byteCount,
                $"Byte count exceeds source span size ({maxByteCount} bytes for {data.Length}x{typeof(T).Name}).");
        }

        if (GpuResourceManagerSystem.IsInitialized
            && GpuResourceManagerSystem.IsRenderThreadKnown
            && !GpuResourceManagerSystem.IsRenderThread)
        {
            EnqueueUpload(PendingUpload.ForBufferData(CopyToPooledBytes(data, byteCount), byteCount));
            return;
        }

        if (byteCount == 0)
        {
            GpuBufferUploader.Allocate(this, 0, IntPtr.Zero, allocationUsage);

            return;
        }

        fixed (T* ptr = data)
        {
            GpuBufferUploader.Allocate(this, byteCount, (IntPtr)ptr, allocationUsage);
        }

    }

    public unsafe bool TryUploadData<T>(ReadOnlySpan<T> data, int byteCount) where T : unmanaged
    {
        if (!IsValid)
        {
            return false;
        }

        try
        {
            UploadData(data, byteCount);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void UploadOrResize<T>(T[] data, int byteCount, bool growExponentially = true) where T : struct
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuBufferObject] Attempted to UploadOrResize on disposed or invalid buffer");
            return;
        }

        ArgumentNullException.ThrowIfNull(data);

        if (byteCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteCount), byteCount, "Byte count must be >= 0.");
        }

        if (byteCount == 0)
        {
            return;
        }

        int maxByteCount = checked(data.Length * Marshal.SizeOf<T>());
        if (byteCount > maxByteCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(byteCount),
                byteCount,
                $"Byte count exceeds source array size ({maxByteCount} bytes for {data.Length}x{typeof(T).Name}).");
        }

        if (GpuResourceManagerSystem.IsInitialized
            && GpuResourceManagerSystem.IsRenderThreadKnown
            && !GpuResourceManagerSystem.IsRenderThread)
        {
            EnqueueUpload(PendingUpload.ForUploadOrResize(CopyToPooledBytes(data, byteCount), byteCount, growExponentially));
            return;
        }

        if (sizeBytes < byteCount)
        {
            EnsureCapacity(byteCount, growExponentially);
        }

        if (TryGetBlittableArrayPointer(data, out GCHandle handle, out IntPtr ptr))
        {
            try
            {
                GpuBufferUploader.Upload(this, 0, byteCount, ptr);
                return;
            }
            finally
            {
                if (handle.IsAllocated)
                {
                    handle.Free();
                }
            }
        }

        GpuBufferUploader.Upload(this, 0, byteCount, data);
    }

    public bool TryUploadOrResize<T>(T[] data, int byteCount, bool growExponentially = true) where T : struct
    {
        if (!IsValid || data is null)
        {
            return false;
        }

        try
        {
            UploadOrResize(data, byteCount, growExponentially);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void UploadOrResize<T>(T[] data, bool growExponentially = true) where T : struct
    {
        ArgumentNullException.ThrowIfNull(data);
        UploadOrResize(data, checked(data.Length * Marshal.SizeOf<T>()), growExponentially);
    }

    public bool TryUploadOrResize<T>(T[] data, bool growExponentially = true) where T : struct
    {
        if (!IsValid || data is null)
        {
            return false;
        }

        try
        {
            UploadOrResize(data, growExponentially);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public unsafe void UploadOrResize<T>(ReadOnlySpan<T> data, int byteCount, bool growExponentially = true) where T : unmanaged
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuBufferObject] Attempted to UploadOrResize on disposed or invalid buffer");
            return;
        }

        if (byteCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteCount), byteCount, "Byte count must be >= 0.");
        }

        if (byteCount == 0)
        {
            return;
        }

        int maxByteCount = checked(data.Length * sizeof(T));
        if (byteCount > maxByteCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(byteCount),
                byteCount,
                $"Byte count exceeds source span size ({maxByteCount} bytes for {data.Length}x{typeof(T).Name}).");
        }

        if (GpuResourceManagerSystem.IsInitialized
            && GpuResourceManagerSystem.IsRenderThreadKnown
            && !GpuResourceManagerSystem.IsRenderThread)
        {
            EnqueueUpload(PendingUpload.ForUploadOrResize(CopyToPooledBytes(data, byteCount), byteCount, growExponentially));
            return;
        }

        if (sizeBytes < byteCount)
        {
            EnsureCapacity(byteCount, growExponentially);
        }

        fixed (T* ptr = data)
        {
            GpuBufferUploader.Upload(this, 0, byteCount, (IntPtr)ptr);
        }
    }

    public unsafe bool TryUploadOrResize<T>(ReadOnlySpan<T> data, int byteCount, bool growExponentially = true) where T : unmanaged
    {
        if (!IsValid)
        {
            return false;
        }

        try
        {
            UploadOrResize(data, byteCount, growExponentially);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public unsafe void UploadOrResize<T>(ReadOnlySpan<T> data, bool growExponentially = true) where T : unmanaged
    {
        UploadOrResize(data, checked(data.Length * sizeof(T)), growExponentially);
    }

    public unsafe bool TryUploadOrResize<T>(ReadOnlySpan<T> data, bool growExponentially = true) where T : unmanaged
    {
        if (!IsValid)
        {
            return false;
        }

        try
        {
            UploadOrResize(data, growExponentially);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void UploadSubData<T>(T[] data, int dstOffsetBytes, int byteCount) where T : struct
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuBufferObject] Attempted to UploadSubData on disposed or invalid buffer");
            return;
        }

        ArgumentNullException.ThrowIfNull(data);

        if (dstOffsetBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dstOffsetBytes), dstOffsetBytes, "Offset must be >= 0.");
        }

        if (byteCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteCount), byteCount, "Byte count must be >= 0.");
        }

        if (byteCount == 0)
        {
            return;
        }

        int maxByteCount = checked(data.Length * Marshal.SizeOf<T>());
        if (byteCount > maxByteCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(byteCount),
                byteCount,
                $"Byte count exceeds source array size ({maxByteCount} bytes for {data.Length}x{typeof(T).Name}).");
        }

        if (sizeBytes < dstOffsetBytes + byteCount)
        {
            throw new InvalidOperationException(
                $"UploadSubData range [{dstOffsetBytes}, {dstOffsetBytes + byteCount}) exceeds allocated buffer size {sizeBytes}.");
        }

        if (GpuResourceManagerSystem.IsInitialized
            && GpuResourceManagerSystem.IsRenderThreadKnown
            && !GpuResourceManagerSystem.IsRenderThread)
        {
            EnqueueUpload(PendingUpload.ForBufferSubData(CopyToPooledBytes(data, byteCount), dstOffsetBytes, byteCount));
            return;
        }

        if (TryGetBlittableArrayPointer(data, out GCHandle handle, out IntPtr ptr))
        {
            try
            {
                GpuBufferUploader.Upload(this, dstOffsetBytes, byteCount, ptr);
                return;
            }
            finally
            {
                if (handle.IsAllocated)
                {
                    handle.Free();
                }
            }
        }

        GpuBufferUploader.Upload(this, dstOffsetBytes, byteCount, data);
    }

    public bool TryUploadSubData<T>(T[] data, int dstOffsetBytes, int byteCount) where T : struct
    {
        if (!IsValid || data is null)
        {
            return false;
        }

        try
        {
            UploadSubData(data, dstOffsetBytes, byteCount);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void UploadSubData<T>(T[] data, int dstOffsetBytes) where T : struct
    {
        ArgumentNullException.ThrowIfNull(data);
        UploadSubData(data, dstOffsetBytes, checked(data.Length * Marshal.SizeOf<T>()));
    }

    public bool TryUploadSubData<T>(T[] data, int dstOffsetBytes) where T : struct
    {
        if (!IsValid || data is null)
        {
            return false;
        }

        try
        {
            UploadSubData(data, dstOffsetBytes);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public unsafe void UploadSubData<T>(ReadOnlySpan<T> data, int dstOffsetBytes, int byteCount) where T : unmanaged
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuBufferObject] Attempted to UploadSubData on disposed or invalid buffer");
            return;
        }

        if (dstOffsetBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dstOffsetBytes), dstOffsetBytes, "Offset must be >= 0.");
        }

        if (byteCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteCount), byteCount, "Byte count must be >= 0.");
        }

        if (byteCount == 0)
        {
            return;
        }

        int maxByteCount = checked(data.Length * sizeof(T));
        if (byteCount > maxByteCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(byteCount),
                byteCount,
                $"Byte count exceeds source span size ({maxByteCount} bytes for {data.Length}x{typeof(T).Name}).");
        }

        if (sizeBytes < dstOffsetBytes + byteCount)
        {
            throw new InvalidOperationException(
                $"UploadSubData range [{dstOffsetBytes}, {dstOffsetBytes + byteCount}) exceeds allocated buffer size {sizeBytes}.");
        }

        if (GpuResourceManagerSystem.IsInitialized
            && GpuResourceManagerSystem.IsRenderThreadKnown
            && !GpuResourceManagerSystem.IsRenderThread)
        {
            EnqueueUpload(PendingUpload.ForBufferSubData(CopyToPooledBytes(data, byteCount), dstOffsetBytes, byteCount));
            return;
        }

        fixed (T* ptr = data)
        {
            GpuBufferUploader.Upload(this, dstOffsetBytes, byteCount, (IntPtr)ptr);
        }
    }

    public unsafe bool TryUploadSubData<T>(ReadOnlySpan<T> data, int dstOffsetBytes, int byteCount) where T : unmanaged
    {
        if (!IsValid)
        {
            return false;
        }

        try
        {
            UploadSubData(data, dstOffsetBytes, byteCount);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public unsafe void UploadSubData<T>(ReadOnlySpan<T> data, int dstOffsetBytes) where T : unmanaged
    {
        UploadSubData(data, dstOffsetBytes, checked(data.Length * sizeof(T)));
    }

    public unsafe bool TryUploadSubData<T>(ReadOnlySpan<T> data, int dstOffsetBytes) where T : unmanaged
    {
        if (!IsValid)
        {
            return false;
        }

        try
        {
            UploadSubData(data, dstOffsetBytes);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public unsafe MappedRange<T> MapRange<T>(int dstOffsetBytes, int elementCount, RHIBufferMapAccess access) where T : unmanaged
    {
        if (!IsValid)
        {
            throw new InvalidOperationException("Cannot map buffer: buffer is not valid.");
        }

        if (dstOffsetBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dstOffsetBytes), dstOffsetBytes, "Offset must be >= 0.");
        }

        if (elementCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(elementCount), elementCount, "Element count must be >= 0.");
        }

        int byteCount = checked(elementCount * sizeof(T));
        if (byteCount == 0)
        {
            return new MappedRange<T>(this, access, dstOffsetBytes, elementCount, IntPtr.Zero, isMapped: false);
        }

        if (sizeBytes < checked(dstOffsetBytes + byteCount))
        {
            throw new InvalidOperationException(
                $"MapRange range [{dstOffsetBytes}, {dstOffsetBytes + byteCount}) exceeds allocated buffer size {sizeBytes}.");
        }

        nint ptr = RHICommandContext.Current.MapBuffer(this, dstOffsetBytes, byteCount, access);
        if (ptr == 0) throw new InvalidOperationException("Named buffer mapping failed.");
        return new MappedRange<T>(this, access, dstOffsetBytes, elementCount, ptr, isMapped: true);
    }

    /// <summary>
    /// Maps a range of the buffer and returns an RAII scope that will unmap on dispose.
    /// </summary>
    public GpuMappedBufferScope<T> MapScope<T>(int dstOffsetBytes, int elementCount, RHIBufferMapAccess access) where T : unmanaged
    {
        return new GpuMappedBufferScope<T>(MapRange<T>(dstOffsetBytes, elementCount, access));
    }

    /// <summary>
    /// Attempts to map a range of the buffer and returns an RAII scope that will unmap on dispose.
    /// </summary>
    public bool TryMapScope<T>(
        int dstOffsetBytes,
        int elementCount,
        RHIBufferMapAccess access,
        out GpuMappedBufferScope<T> scope) where T : unmanaged
    {
        if (TryMapRange(dstOffsetBytes, elementCount, access, out MappedRange<T> range))
        {
            scope = new GpuMappedBufferScope<T>(range);
            return scope.IsMapped;
        }

        scope = default;
        return false;
    }

    public bool TryMapRange<T>(int dstOffsetBytes, int elementCount, RHIBufferMapAccess access, out MappedRange<T> range) where T : unmanaged
    {
        try
        {
            range = MapRange<T>(dstOffsetBytes, elementCount, access);
            return range.IsMapped;
        }
        catch
        {
            range = default;
            return false;
        }
    }

    protected override void OnDetached(nint id)
    {
        DropPendingUploads();
        Volatile.Write(ref uploadsQueued, 0);

    }

    protected override void OnAfterDelete()
    {
        DropPendingUploads();
        Volatile.Write(ref uploadsQueued, 0);

    }

    public override string ToString()
    {
        return $"{GetType().Name}(id={bufferId}, target={target}, sizeBytes={sizeBytes}, usage={usage}, name={debugName}, disposed={IsDisposed})";
    }



    private static bool TryGetBlittableArrayPointer<T>(T[] data, out GCHandle handle, out IntPtr ptr) where T : struct
    {
        handle = default;
        ptr = IntPtr.Zero;

        if (data.Length == 0)
        {
            return true;
        }

        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            return false;
        }

        try
        {
            handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            ptr = handle.AddrOfPinnedObject();
            return true;
        }
        catch
        {
            if (handle.IsAllocated)
            {
                handle.Free();
            }

            ptr = IntPtr.Zero;
            return false;
        }
    }

    internal void DrainPendingUploadsOnRenderThread()
    {
        if (!IsValid)
        {
            DropPendingUploads();
            Volatile.Write(ref uploadsQueued, 0);
            return;
        }

        while (pendingUploads.TryDequeue(out PendingUpload upload))
        {
            try
            {
                ExecutePendingUploadOnRenderThread(upload);
            }
            finally
            {
                upload.Return();
            }
        }

        Volatile.Write(ref uploadsQueued, 0);

        if (!pendingUploads.IsEmpty && Interlocked.Exchange(ref uploadsQueued, 1) == 0)
        {
            GpuResourceManagerSystem.EnqueueBufferUpload(this);
        }
    }

    private void EnqueueUpload(in PendingUpload upload)
    {
        pendingUploads.Enqueue(upload);

        if (Interlocked.Exchange(ref uploadsQueued, 1) == 0)
        {
            GpuResourceManagerSystem.EnqueueBufferUpload(this);
        }
    }

    private void DropPendingUploads()
    {
        while (pendingUploads.TryDequeue(out PendingUpload upload))
        {
            upload.Return();
        }
    }

    private void ExecutePendingUploadOnRenderThread(in PendingUpload upload)
    {
        if (upload.Kind == PendingUploadKind.UploadOrResize)
        {
            EnsureCapacity(upload.ByteCount, upload.GrowExponentially);
        }

        if (upload.Kind == PendingUploadKind.BufferData)
        {
            if (upload.ByteCount == 0)
            {
                GpuBufferUploader.Allocate(this, 0, IntPtr.Zero, allocationUsage);

                return;
            }

            if (upload.Buffer is null)
            {
                return;
            }

            unsafe
            {
                fixed (byte* ptr = upload.Buffer)
                {
                    GpuBufferUploader.Allocate(this, upload.ByteCount, (IntPtr)ptr, allocationUsage);
                }
            }

            return;
        }

        if (upload.ByteCount == 0 || upload.Buffer is null)
        {
            return;
        }

        unsafe
        {
            fixed (byte* ptr = upload.Buffer)
            {
                GpuBufferUploader.Upload(this, upload.DstOffsetBytes, upload.ByteCount, (IntPtr)ptr);
            }
        }
    }

    private static byte[]? CopyToPooledBytes<T>(T[] data, int byteCount) where T : struct
    {
        if (byteCount <= 0)
        {
            return null;
        }

        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            throw new InvalidOperationException($"Cannot stage buffer upload for non-blittable element type {typeof(T).Name}.");
        }

        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(new ReadOnlySpan<T>(data));
        byte[] rented = ArrayPool<byte>.Shared.Rent(byteCount);
        bytes.Slice(0, byteCount).CopyTo(rented.AsSpan(0, byteCount));
        return rented;
    }

    private static byte[]? CopyToPooledBytes<T>(ReadOnlySpan<T> data, int byteCount) where T : unmanaged
    {
        if (byteCount <= 0)
        {
            return null;
        }

        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(data);
        byte[] rented = ArrayPool<byte>.Shared.Rent(byteCount);
        bytes.Slice(0, byteCount).CopyTo(rented.AsSpan(0, byteCount));
        return rented;
    }

    private readonly struct PendingUpload
    {
        public PendingUploadKind Kind { get; }
        public byte[]? Buffer { get; }
        public int DstOffsetBytes { get; }
        public int ByteCount { get; }
        public bool GrowExponentially { get; }

        private PendingUpload(PendingUploadKind kind, byte[]? buffer, int dstOffsetBytes, int byteCount, bool growExponentially)
        {
            Kind = kind;
            Buffer = buffer;
            DstOffsetBytes = dstOffsetBytes;
            ByteCount = byteCount;
            GrowExponentially = growExponentially;
        }

        public static PendingUpload ForBufferData(byte[]? buffer, int byteCount)
            => new(PendingUploadKind.BufferData, buffer, dstOffsetBytes: 0, byteCount, growExponentially: false);

        public static PendingUpload ForBufferSubData(byte[]? buffer, int dstOffsetBytes, int byteCount)
            => new(PendingUploadKind.BufferSubData, buffer, dstOffsetBytes, byteCount, growExponentially: false);

        public static PendingUpload ForUploadOrResize(byte[]? buffer, int byteCount, bool growExponentially)
            => new(PendingUploadKind.UploadOrResize, buffer, dstOffsetBytes: 0, byteCount, growExponentially);

        public void Return()
        {
            if (Buffer is null)
            {
                return;
            }

            ArrayPool<byte>.Shared.Return(Buffer);
        }
    }

    private enum PendingUploadKind
    {
        BufferData = 0,
        BufferSubData = 1,
        UploadOrResize = 2,
    }

    /// <summary>Owns an immediate mapping against its original wrapper, identity and context.</summary>
    public struct MappedRange<T> : IDisposable where T : unmanaged
    {
        private readonly GpuBufferObject owner;
        private readonly RHICommandContext context;
        private readonly RHIBufferMapAccess access;
        private readonly int nativeId;
        private readonly int offsetBytes;
        private readonly int elementCount;
        private readonly nint pointer;
        private bool isMapped;

        #region Public API
        /// <summary>Gets the original mapped name for diagnostics only.</summary>
        public int BufferId => nativeId;
        /// <summary>Gets the allocation-relative offset.</summary>
        public int OffsetBytes => offsetBytes;
        /// <summary>Gets the mapped element extent.</summary>
        public int ElementCount => elementCount;
        /// <summary>Gets the checked byte extent.</summary>
        public unsafe int ByteCount => checked(elementCount * sizeof(T));
        /// <summary>Reports whether this value still owns its mapping.</summary>
        public bool IsMapped => isMapped;
        /// <summary>Gets the immediate client pointer; it must never be retained by a recorded command.</summary>
        public nint Pointer => pointer;
        /// <summary>Provides client access only while the original allocation remains live.</summary>
        public unsafe Span<T> Span { get { if (!isMapped) return Span<T>.Empty; RequireOwner(); return new((void*)pointer, elementCount); } }
        /// <summary>Flushes all bytes when explicit flush was requested.</summary>
        public void Flush() => Flush(0, ByteCount);
        /// <summary>Flushes a checked subrange without changing selectors.</summary>
        public void Flush(int relativeOffsetBytes, int byteCount)
        {
            if (!isMapped) return;
            RequireOwner();
            if (relativeOffsetBytes < 0 || byteCount < 0 || relativeOffsetBytes > ByteCount || byteCount > ByteCount-relativeOffsetBytes)
                throw new ArgumentOutOfRangeException(nameof(byteCount));
            if ((access & RHIBufferMapAccess.ExplicitFlush) != 0 && byteCount != 0)
                context.FlushMappedBuffer(owner, relativeOffsetBytes, byteCount);
        }
        /// <summary>Unmaps exactly once against the original context, never retrying after failure.</summary>
        public void Dispose()
        {
            if (!isMapped) return;
            RequireOwner();
            isMapped = false;
            if (!context.UnmapBuffer(owner)) throw new InvalidOperationException("Mapped buffer contents became invalid.");
        }
        #endregion

        #region Internal API
        /// <summary>Retains mapping ownership without querying metadata or capturing another registration.</summary>
        internal MappedRange(GpuBufferObject owner, RHIBufferMapAccess access, int offsetBytes, int elementCount, nint pointer, bool isMapped)
        {
            this.owner=owner; this.access=access; this.offsetBytes=offsetBytes; this.elementCount=elementCount;
            this.pointer=pointer; this.isMapped=isMapped; nativeId=owner.BufferId; context=RHICommandContext.Current;
        }
        #endregion

        #region Private
        /// <summary>Rejects disposal or replacement before touching mapping storage.</summary>
        private void RequireOwner()
        {
            context.OpenGL.RequireContext();
            if (!owner.IsValid || owner.BufferId != nativeId) throw new InvalidOperationException("The mapped allocation has retired.");
        }
        #endregion
    }

    /// <summary>Preserves the public buffer scope API while delegating restoration to the state cache.</summary>
    public readonly struct BindingScope : IDisposable
    {
        private readonly StateCache.BufferScope scope;

        /// <summary>Retains the state cache's original selector and VAO association.</summary>
        internal BindingScope(StateCache.BufferScope scope)
        {
            this.scope = scope;
        }

        /// <summary>Restores the captured buffer association through the shared cache contract.</summary>
        public void Dispose() => scope.Dispose();
    }
}
