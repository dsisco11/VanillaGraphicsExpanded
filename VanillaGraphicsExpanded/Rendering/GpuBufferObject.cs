using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Base class for OpenGL buffer object wrappers.
/// All methods require a current GL context on the calling thread.
/// </summary>
internal abstract class GpuBufferObject : GpuResource, IDisposable
{
    protected int bufferId;
    protected int sizeBytes;
    protected BufferTarget target;
    protected BufferUsageHint usage;
    protected string? debugName;

    public int BufferId => bufferId;
    public int SizeBytes => sizeBytes;
    public BufferTarget Target => target;
    public BufferUsageHint Usage => usage;
    public string? DebugName => debugName;

    protected override int ResourceId
    {
        get => bufferId;
        set => bufferId = value;
    }

    protected override GpuResourceKind ResourceKind => GpuResourceKind.Buffer;

    protected static int CreateBufferId(string? debugName)
    {
        int id = GL.GenBuffer();
        if (id == 0)
        {
            throw new InvalidOperationException("glGenBuffers failed.");
        }

#if DEBUG
        GlDebug.TryLabel(ObjectLabelIdentifier.Buffer, id, debugName);
#endif

        return id;
    }

    public void SetDebugName(string? debugName)
    {
        this.debugName = debugName;

#if DEBUG
        if (bufferId != 0)
        {
            GlDebug.TryLabel(ObjectLabelIdentifier.Buffer, bufferId, debugName);
        }
#endif
    }

    public BindingScope BindScope()
    {
        int previous = 0;
        int previousVao = 0;

        // ElementArrayBuffer is VAO state. Preserve the VAO binding so restore happens on the same VAO.
        if (target == BufferTarget.ElementArrayBuffer)
        {
            try
            {
                GL.GetInteger(GetPName.VertexArrayBinding, out previousVao);
            }
            catch
            {
                previousVao = 0;
            }
        }

        try
        {
            if (TryGetBindingQuery(target, out GetPName pname))
            {
                GL.GetInteger(pname, out previous);
            }
        }
        catch
        {
            previous = 0;
        }

        Bind();
        return new BindingScope(target, previous, previousVao);
    }

    public void Bind()
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuBufferObject] Attempted to bind disposed or invalid buffer");
            return;
        }

        GL.BindBuffer(target, bufferId);
    }

    public bool TryBind()
    {
        if (!IsValid)
        {
            return false;
        }

        GL.BindBuffer(target, bufferId);
        return true;
    }

    public void Unbind()
    {
        GL.BindBuffer(target, 0);
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

        if (!TryDsaBufferData(sizeBytes, IntPtr.Zero))
        {
            Bind();
            GL.BufferData(target, sizeBytes, IntPtr.Zero, usage);
            Unbind();
        }

        this.sizeBytes = sizeBytes;
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

        if (!TryDsaBufferData(newCapacity, IntPtr.Zero))
        {
            Bind();
            GL.BufferData(target, newCapacity, IntPtr.Zero, usage);
            Unbind();
        }

        sizeBytes = newCapacity;
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

        if (TryGetBlittableArrayPointer(data, out GCHandle handle, out IntPtr ptr))
        {
            try
            {
                if (TryDsaBufferData(byteCount, ptr))
                {
                    sizeBytes = byteCount;
                    return;
                }
            }
            finally
            {
                if (handle.IsAllocated)
                {
                    handle.Free();
                }
            }
        }

        Bind();
        GL.BufferData(target, byteCount, data, usage);
        Unbind();

        sizeBytes = byteCount;
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

        if (byteCount == 0)
        {
            if (!TryDsaBufferData(0, IntPtr.Zero))
            {
                Bind();
                GL.BufferData(target, IntPtr.Zero, IntPtr.Zero, usage);
                Unbind();
            }

            sizeBytes = 0;
            return;
        }

        fixed (T* ptr = data)
        {
            if (!TryDsaBufferData(byteCount, (IntPtr)ptr))
            {
                Bind();
                GL.BufferData(target, (IntPtr)byteCount, (IntPtr)ptr, usage);
                Unbind();
            }
        }

        sizeBytes = byteCount;
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

        if (sizeBytes < byteCount)
        {
            EnsureCapacity(byteCount, growExponentially);
        }

        if (TryGetBlittableArrayPointer(data, out GCHandle handle, out IntPtr ptr))
        {
            try
            {
                if (TryDsaBufferSubData(0, byteCount, ptr))
                {
                    return;
                }
            }
            finally
            {
                if (handle.IsAllocated)
                {
                    handle.Free();
                }
            }
        }

        Bind();
        GL.BufferSubData(target, (IntPtr)0, (IntPtr)byteCount, data);
        Unbind();
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

        if (sizeBytes < byteCount)
        {
            EnsureCapacity(byteCount, growExponentially);
        }

        fixed (T* ptr = data)
        {
            if (!TryDsaBufferSubData(0, byteCount, (IntPtr)ptr))
            {
                Bind();
                GL.BufferSubData(target, IntPtr.Zero, (IntPtr)byteCount, (IntPtr)ptr);
                Unbind();
            }
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

        if (TryGetBlittableArrayPointer(data, out GCHandle handle, out IntPtr ptr))
        {
            try
            {
                if (TryDsaBufferSubData(dstOffsetBytes, byteCount, ptr))
                {
                    return;
                }
            }
            finally
            {
                if (handle.IsAllocated)
                {
                    handle.Free();
                }
            }
        }

        Bind();
        GL.BufferSubData(target, (IntPtr)dstOffsetBytes, (IntPtr)byteCount, data);
        Unbind();
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

        fixed (T* ptr = data)
        {
            if (!TryDsaBufferSubData(dstOffsetBytes, byteCount, (IntPtr)ptr))
            {
                Bind();
                GL.BufferSubData(target, (IntPtr)dstOffsetBytes, (IntPtr)byteCount, (IntPtr)ptr);
                Unbind();
            }
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

    public unsafe MappedRange<T> MapRange<T>(int dstOffsetBytes, int elementCount, MapBufferAccessMask access) where T : unmanaged
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
            return new MappedRange<T>(bufferId, target, access, dstOffsetBytes, elementCount, IntPtr.Zero, isMapped: false);
        }

        if (sizeBytes < checked(dstOffsetBytes + byteCount))
        {
            throw new InvalidOperationException(
                $"MapRange range [{dstOffsetBytes}, {dstOffsetBytes + byteCount}) exceeds allocated buffer size {sizeBytes}.");
        }

        if (BufferMapDsa.TryMapNamedBufferRange(bufferId, dstOffsetBytes, byteCount, access, out IntPtr ptr))
        {
            return new MappedRange<T>(bufferId, target, access, dstOffsetBytes, elementCount, ptr, isMapped: true);
        }

        if (target == BufferTarget.ElementArrayBuffer)
        {
            ptr = MapElementArrayBufferRange(bufferId, dstOffsetBytes, byteCount, access);
        }
        else
        {
            using var scope = BindScope();
            ptr = GL.MapBufferRange(target, (IntPtr)dstOffsetBytes, (IntPtr)byteCount, access);
        }

        if (ptr == IntPtr.Zero)
        {
            throw new InvalidOperationException("glMapBufferRange failed.");
        }

        return new MappedRange<T>(bufferId, target, access, dstOffsetBytes, elementCount, ptr, isMapped: true);
    }

    public bool TryMapRange<T>(int dstOffsetBytes, int elementCount, MapBufferAccessMask access, out MappedRange<T> range) where T : unmanaged
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

    protected override void OnDetached(int id)
    {
        sizeBytes = 0;
    }

    protected override void OnAfterDelete()
    {
        sizeBytes = 0;
    }

    public override string ToString()
    {
        return $"{GetType().Name}(id={bufferId}, target={target}, sizeBytes={sizeBytes}, usage={usage}, name={debugName}, disposed={IsDisposed})";
    }

    private static IntPtr MapElementArrayBufferRange(int bufferId, int offsetBytes, int byteCount, MapBufferAccessMask access)
    {
        int previousVao = 0;
        try
        {
            GL.GetInteger(GetPName.VertexArrayBinding, out previousVao);
        }
        catch
        {
            previousVao = 0;
        }

        GL.BindVertexArray(0);

        int previousEbo0 = 0;
        try
        {
            GL.GetInteger(GetPName.ElementArrayBufferBinding, out previousEbo0);
        }
        catch
        {
            previousEbo0 = 0;
        }

        GL.BindBuffer(BufferTarget.ElementArrayBuffer, bufferId);

        IntPtr ptr = GL.MapBufferRange(BufferTarget.ElementArrayBuffer, (IntPtr)offsetBytes, (IntPtr)byteCount, access);

        GL.BindBuffer(BufferTarget.ElementArrayBuffer, previousEbo0);
        GL.BindVertexArray(previousVao);

        return ptr;
    }

    private static bool TryGetBindingQuery(BufferTarget target, out GetPName pname)
    {
        pname = target switch
        {
            BufferTarget.ArrayBuffer => GetPName.ArrayBufferBinding,
            BufferTarget.ElementArrayBuffer => GetPName.ElementArrayBufferBinding,
            BufferTarget.PixelPackBuffer => GetPName.PixelPackBufferBinding,
            BufferTarget.PixelUnpackBuffer => GetPName.PixelUnpackBufferBinding,
            BufferTarget.UniformBuffer => GetPName.UniformBufferBinding,
            _ => default
        };

        return pname != default;
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

    private bool TryDsaBufferData(int byteCount, IntPtr data)
    {
        return bufferId != 0 && BufferDsa.TryNamedBufferData(bufferId, byteCount, data, usage);
    }

    private bool TryDsaBufferSubData(int dstOffsetBytes, int byteCount, IntPtr data)
    {
        return bufferId != 0 && BufferDsa.TryNamedBufferSubData(bufferId, dstOffsetBytes, byteCount, data);
    }

    private static class BufferMapDsa
    {
        private static int enabledState;

        public static bool TryMapNamedBufferRange(int bufferId, int offsetBytes, int byteCount, MapBufferAccessMask access, out IntPtr ptr)
        {
            ptr = IntPtr.Zero;

            if (enabledState == -1)
            {
                return false;
            }

            try
            {
                ptr = GL.MapNamedBufferRange(bufferId, (IntPtr)offsetBytes, byteCount, (BufferAccessMask)(int)access);

                if (ptr == IntPtr.Zero)
                {
                    return false;
                }

                enabledState = 1;
                return true;
            }
            catch
            {
                enabledState = -1;
                return false;
            }
        }

        public static bool TryUnmapNamedBuffer(int bufferId)
        {
            if (enabledState == -1)
            {
                return false;
            }

            try
            {
                bool ok = GL.UnmapNamedBuffer(bufferId);
                enabledState = 1;
                return ok;
            }
            catch
            {
                enabledState = -1;
                return false;
            }
        }

        public static bool TryFlushMappedNamedBufferRange(int bufferId, int offsetBytes, int byteCount)
        {
            if (enabledState == -1)
            {
                return false;
            }

            try
            {
                GL.FlushMappedNamedBufferRange(bufferId, (IntPtr)offsetBytes, byteCount);

                enabledState = 1;
                return true;
            }
            catch
            {
                enabledState = -1;
                return false;
            }
        }
    }

    public struct MappedRange<T> : IDisposable where T : unmanaged
    {
        private readonly int bufferId;
        private readonly BufferTarget target;
        private readonly MapBufferAccessMask access;
        private readonly int offsetBytes;
        private readonly int elementCount;
        private readonly IntPtr ptr;
        private bool isMapped;

        internal MappedRange(
            int bufferId,
            BufferTarget target,
            MapBufferAccessMask access,
            int offsetBytes,
            int elementCount,
            IntPtr ptr,
            bool isMapped)
        {
            this.bufferId = bufferId;
            this.target = target;
            this.access = access;
            this.offsetBytes = offsetBytes;
            this.elementCount = elementCount;
            this.ptr = ptr;
            this.isMapped = isMapped;
        }

        public int BufferId => bufferId;
        public int OffsetBytes => offsetBytes;
        public int ElementCount => elementCount;
        public unsafe int ByteCount => checked(elementCount * sizeof(T));
        public bool IsMapped => isMapped;
        public IntPtr Pointer => ptr;

        public unsafe Span<T> Span
        {
            get
            {
                if (!isMapped)
                {
                    return Span<T>.Empty;
                }

                return new Span<T>((void*)ptr, elementCount);
            }
        }

        public void Flush()
        {
            Flush(relativeOffsetBytes: 0, byteCount: ByteCount);
        }

        public void Flush(int relativeOffsetBytes, int byteCount)
        {
            if (!isMapped || byteCount <= 0)
            {
                return;
            }

            if ((access & MapBufferAccessMask.MapFlushExplicitBit) == 0)
            {
                return;
            }

            int absOffset = checked(offsetBytes + relativeOffsetBytes);
            if (BufferMapDsa.TryFlushMappedNamedBufferRange(bufferId, absOffset, byteCount))
            {
                return;
            }

            if (target == BufferTarget.ElementArrayBuffer)
            {
                FlushElementArrayBufferRange(bufferId, absOffset, byteCount);
                return;
            }

            FlushBoundRange(bufferId, target, absOffset, byteCount);
        }

        public void Dispose()
        {
            if (!isMapped)
            {
                return;
            }

            isMapped = false;

            if (BufferMapDsa.TryUnmapNamedBuffer(bufferId))
            {
                return;
            }

            if (target == BufferTarget.ElementArrayBuffer)
            {
                UnmapElementArrayBuffer(bufferId);
                return;
            }

            UnmapBound(bufferId, target);
        }

        private static void FlushBoundRange(int bufferId, BufferTarget target, int offsetBytes, int byteCount)
        {
            int previous = 0;
            try
            {
                if (TryGetBindingQuery(target, out GetPName pname))
                {
                    GL.GetInteger(pname, out previous);
                }
            }
            catch
            {
                previous = 0;
            }

            GL.BindBuffer(target, bufferId);
            GL.FlushMappedBufferRange(target, (IntPtr)offsetBytes, (IntPtr)byteCount);
            GL.BindBuffer(target, previous);
        }

        private static void FlushElementArrayBufferRange(int bufferId, int offsetBytes, int byteCount)
        {
            int previousVao = 0;
            try
            {
                GL.GetInteger(GetPName.VertexArrayBinding, out previousVao);
            }
            catch
            {
                previousVao = 0;
            }

            GL.BindVertexArray(0);

            int previousEbo0 = 0;
            try
            {
                GL.GetInteger(GetPName.ElementArrayBufferBinding, out previousEbo0);
            }
            catch
            {
                previousEbo0 = 0;
            }

            GL.BindBuffer(BufferTarget.ElementArrayBuffer, bufferId);
            GL.FlushMappedBufferRange(BufferTarget.ElementArrayBuffer, (IntPtr)offsetBytes, (IntPtr)byteCount);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, previousEbo0);
            GL.BindVertexArray(previousVao);
        }

        private static void UnmapBound(int bufferId, BufferTarget target)
        {
            int previous = 0;
            try
            {
                if (TryGetBindingQuery(target, out GetPName pname))
                {
                    GL.GetInteger(pname, out previous);
                }
            }
            catch
            {
                previous = 0;
            }

            GL.BindBuffer(target, bufferId);
            _ = GL.UnmapBuffer(target);
            GL.BindBuffer(target, previous);
        }

        private static void UnmapElementArrayBuffer(int bufferId)
        {
            int previousVao = 0;
            try
            {
                GL.GetInteger(GetPName.VertexArrayBinding, out previousVao);
            }
            catch
            {
                previousVao = 0;
            }

            GL.BindVertexArray(0);

            int previousEbo0 = 0;
            try
            {
                GL.GetInteger(GetPName.ElementArrayBufferBinding, out previousEbo0);
            }
            catch
            {
                previousEbo0 = 0;
            }

            GL.BindBuffer(BufferTarget.ElementArrayBuffer, bufferId);
            _ = GL.UnmapBuffer(BufferTarget.ElementArrayBuffer);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, previousEbo0);
            GL.BindVertexArray(previousVao);
        }
    }

    private static class BufferDsa
    {
        private static int enabledState;

        public static bool TryNamedBufferData(int bufferId, int byteCount, IntPtr data, BufferUsageHint usage)
        {
            if (enabledState == -1)
            {
                return false;
            }

            try
            {
                GL.NamedBufferData(bufferId, byteCount, data, usage);

                enabledState = 1;
                return true;
            }
            catch
            {
                enabledState = -1;
                return false;
            }
        }

        public static bool TryNamedBufferSubData(int bufferId, int dstOffsetBytes, int byteCount, IntPtr data)
        {
            if (enabledState == -1)
            {
                return false;
            }

            try
            {
                GL.NamedBufferSubData(bufferId, (IntPtr)dstOffsetBytes, byteCount, data);

                enabledState = 1;
                return true;
            }
            catch
            {
                enabledState = -1;
                return false;
            }
        }
    }

    public readonly struct BindingScope : IDisposable
    {
        private readonly BufferTarget target;
        private readonly int previous;
        private readonly int previousVao;

        public BindingScope(BufferTarget target, int previous, int previousVao)
        {
            this.target = target;
            this.previous = previous;
            this.previousVao = previousVao;
        }

        public void Dispose()
        {
            if (target == BufferTarget.ElementArrayBuffer)
            {
                GL.BindVertexArray(previousVao);
            }

            GL.BindBuffer(target, previous);
        }
    }
}
