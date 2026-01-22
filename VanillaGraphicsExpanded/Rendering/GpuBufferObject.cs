using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Reflection;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Base class for OpenGL buffer object wrappers.
/// All methods require a current GL context on the calling thread.
/// </summary>
internal abstract class GpuBufferObject : IDisposable
{
    protected int bufferId;
    protected int sizeBytes;
    protected BufferTarget target;
    protected BufferUsageHint usage;
    protected string? debugName;
    protected bool isDisposed;

    public int BufferId => bufferId;
    public int SizeBytes => sizeBytes;
    public BufferTarget Target => target;
    public BufferUsageHint Usage => usage;
    public string? DebugName => debugName;

    public bool IsDisposed => isDisposed;
    public bool IsValid => bufferId != 0 && !isDisposed;

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

    public virtual int Detach()
    {
        if (isDisposed)
        {
            return 0;
        }

        int id = bufferId;
        bufferId = 0;
        sizeBytes = 0;
        isDisposed = true;
        return id;
    }

    public int ReleaseHandle()
    {
        return Detach();
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

    public virtual void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        if (bufferId != 0)
        {
            GL.DeleteBuffer(bufferId);
            bufferId = 0;
        }

        sizeBytes = 0;
        isDisposed = true;
    }

    public override string ToString()
    {
        return $"{GetType().Name}(id={bufferId}, target={target}, sizeBytes={sizeBytes}, usage={usage}, name={debugName}, disposed={isDisposed})";
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

    private static class BufferDsa
    {
        private static readonly Action<int, IntPtr, IntPtr, BufferUsageHint>? namedBufferDataPtr;
        private static readonly Action<int, int, IntPtr, BufferUsageHint>? namedBufferDataInt;
        private static readonly Action<int, IntPtr, IntPtr, IntPtr>? namedBufferSubDataPtr;
        private static readonly Action<int, int, int, IntPtr>? namedBufferSubDataInt;

        private static int enabledState;

        static BufferDsa()
        {
            namedBufferDataPtr =
                TryCreateNamedBufferDataPtr("NamedBufferData") ??
                TryCreateNamedBufferDataPtr("NamedBufferDataEXT");

            namedBufferDataInt =
                TryCreateNamedBufferDataInt("NamedBufferData") ??
                TryCreateNamedBufferDataInt("NamedBufferDataEXT");

            namedBufferSubDataPtr =
                TryCreateNamedBufferSubDataPtr("NamedBufferSubData") ??
                TryCreateNamedBufferSubDataPtr("NamedBufferSubDataEXT");

            namedBufferSubDataInt =
                TryCreateNamedBufferSubDataInt("NamedBufferSubData") ??
                TryCreateNamedBufferSubDataInt("NamedBufferSubDataEXT");

            if ((namedBufferDataPtr is null && namedBufferDataInt is null)
                || (namedBufferSubDataPtr is null && namedBufferSubDataInt is null))
            {
                enabledState = -1;
            }
        }

        public static bool TryNamedBufferData(int bufferId, int byteCount, IntPtr data, BufferUsageHint usage)
        {
            if (enabledState == -1)
            {
                return false;
            }

            try
            {
                if (namedBufferDataPtr is not null)
                {
                    namedBufferDataPtr(bufferId, (IntPtr)byteCount, data, usage);
                }
                else if (namedBufferDataInt is not null)
                {
                    namedBufferDataInt(bufferId, byteCount, data, usage);
                }
                else
                {
                    enabledState = -1;
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

        public static bool TryNamedBufferSubData(int bufferId, int dstOffsetBytes, int byteCount, IntPtr data)
        {
            if (enabledState == -1)
            {
                return false;
            }

            try
            {
                if (namedBufferSubDataPtr is not null)
                {
                    namedBufferSubDataPtr(bufferId, (IntPtr)dstOffsetBytes, (IntPtr)byteCount, data);
                }
                else if (namedBufferSubDataInt is not null)
                {
                    namedBufferSubDataInt(bufferId, dstOffsetBytes, byteCount, data);
                }
                else
                {
                    enabledState = -1;
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

        private static Action<int, IntPtr, IntPtr, BufferUsageHint>? TryCreateNamedBufferDataPtr(string name)
        {
            MethodInfo? method = typeof(GL).GetMethod(
                name,
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(int), typeof(IntPtr), typeof(IntPtr), typeof(BufferUsageHint) },
                modifiers: null);

            if (method is null)
            {
                return null;
            }

            try
            {
                return (Action<int, IntPtr, IntPtr, BufferUsageHint>)Delegate.CreateDelegate(
                    typeof(Action<int, IntPtr, IntPtr, BufferUsageHint>),
                    method);
            }
            catch
            {
                return null;
            }
        }

        private static Action<int, int, IntPtr, BufferUsageHint>? TryCreateNamedBufferDataInt(string name)
        {
            MethodInfo? method = typeof(GL).GetMethod(
                name,
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(int), typeof(int), typeof(IntPtr), typeof(BufferUsageHint) },
                modifiers: null);

            if (method is null)
            {
                return null;
            }

            try
            {
                return (Action<int, int, IntPtr, BufferUsageHint>)Delegate.CreateDelegate(
                    typeof(Action<int, int, IntPtr, BufferUsageHint>),
                    method);
            }
            catch
            {
                return null;
            }
        }

        private static Action<int, IntPtr, IntPtr, IntPtr>? TryCreateNamedBufferSubDataPtr(string name)
        {
            MethodInfo? method = typeof(GL).GetMethod(
                name,
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(int), typeof(IntPtr), typeof(IntPtr), typeof(IntPtr) },
                modifiers: null);

            if (method is null)
            {
                return null;
            }

            try
            {
                return (Action<int, IntPtr, IntPtr, IntPtr>)Delegate.CreateDelegate(
                    typeof(Action<int, IntPtr, IntPtr, IntPtr>),
                    method);
            }
            catch
            {
                return null;
            }
        }

        private static Action<int, int, int, IntPtr>? TryCreateNamedBufferSubDataInt(string name)
        {
            MethodInfo? method = typeof(GL).GetMethod(
                name,
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(int), typeof(int), typeof(int), typeof(IntPtr) },
                modifiers: null);

            if (method is null)
            {
                return null;
            }

            try
            {
                return (Action<int, int, int, IntPtr>)Delegate.CreateDelegate(
                    typeof(Action<int, int, int, IntPtr>),
                    method);
            }
            catch
            {
                return null;
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
