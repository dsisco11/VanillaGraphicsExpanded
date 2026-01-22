using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

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
        return new BindingScope(target, previous);
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

        Bind();
        GL.BufferData(target, sizeBytes, IntPtr.Zero, usage);
        Unbind();

        this.sizeBytes = sizeBytes;
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

        Bind();
        GL.BufferData(target, newCapacity, IntPtr.Zero, usage);
        Unbind();

        sizeBytes = newCapacity;
    }

    public void UploadData<T>(T[] data) where T : struct
    {
        ArgumentNullException.ThrowIfNull(data);
        UploadData(data, checked(data.Length * Marshal.SizeOf<T>()));
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

        Bind();
        GL.BufferData(target, byteCount, data, usage);
        Unbind();

        sizeBytes = byteCount;
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

        Bind();
        GL.BufferSubData(target, (IntPtr)0, (IntPtr)byteCount, data);
        Unbind();
    }

    public void UploadOrResize<T>(T[] data, bool growExponentially = true) where T : struct
    {
        ArgumentNullException.ThrowIfNull(data);
        UploadOrResize(data, checked(data.Length * Marshal.SizeOf<T>()), growExponentially);
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

        Bind();
        GL.BufferSubData(target, (IntPtr)dstOffsetBytes, (IntPtr)byteCount, data);
        Unbind();
    }

    public void UploadSubData<T>(T[] data, int dstOffsetBytes) where T : struct
    {
        ArgumentNullException.ThrowIfNull(data);
        UploadSubData(data, dstOffsetBytes, checked(data.Length * Marshal.SizeOf<T>()));
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

    public readonly struct BindingScope : IDisposable
    {
        private readonly BufferTarget target;
        private readonly int previous;

        public BindingScope(BufferTarget target, int previous)
        {
            this.target = target;
            this.previous = previous;
        }

        public void Dispose()
        {
            GL.BindBuffer(target, previous);
        }
    }
}
