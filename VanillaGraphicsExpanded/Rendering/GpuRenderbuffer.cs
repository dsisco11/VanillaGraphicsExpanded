using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an OpenGL renderbuffer object (RBO).
/// Deletion is deferred to <see cref="GpuResourceManager"/> when available.
/// All methods require a current GL context on the calling thread.
/// </summary>
public sealed class GpuRenderbuffer : GpuResource, IDisposable
{
    private int renderbufferId;
    private readonly bool ownsRenderbuffer;
    private string? debugName;

    private RenderbufferStorage storage;
    private int width;
    private int height;
    private int samples;

    /// <summary>
    /// Gets the underlying OpenGL renderbuffer id.
    /// </summary>
    public int RenderbufferId => renderbufferId;

    /// <summary>
    /// Gets the last allocated storage format (if any).
    /// </summary>
    public RenderbufferStorage Storage => storage;

    /// <summary>
    /// Gets the last allocated width (if any).
    /// </summary>
    public int Width => width;

    /// <summary>
    /// Gets the last allocated height (if any).
    /// </summary>
    public int Height => height;

    /// <summary>
    /// Gets the last allocated sample count (0 for non-multisampled).
    /// </summary>
    public int Samples => samples;

    /// <summary>
    /// Gets the debug name used for KHR_debug labeling (debug builds only).
    /// </summary>
    public string? DebugName => debugName;

    protected override nint ResourceId
    {
        get => renderbufferId;
        set => renderbufferId = (int)value;
    }

    protected override GpuResourceKind ResourceKind => GpuResourceKind.Renderbuffer;

    protected override bool OwnsResource => ownsRenderbuffer;

    /// <summary>
    /// Returns <c>true</c> when the renderbuffer has a non-zero id and has not been disposed.
    /// </summary>
    public new bool IsValid => renderbufferId != 0 && !IsDisposed;

    private GpuRenderbuffer(int renderbufferId, bool ownsRenderbuffer, string? debugName)
    {
        this.renderbufferId = renderbufferId;
        this.ownsRenderbuffer = ownsRenderbuffer;
        this.debugName = debugName;
    }

    /// <summary>
    /// Creates a new renderbuffer and allocates storage.
    /// </summary>
    public static GpuRenderbuffer Create(
        RenderbufferStorage storage,
        int width,
        int height,
        int samples = 0,
        string? debugName = null)
    {
        int id = GL.GenRenderbuffer();
        if (id == 0)
        {
            throw new InvalidOperationException("glGenRenderbuffers failed.");
        }

#if DEBUG
        GlDebug.TryLabel(ObjectLabelIdentifier.Renderbuffer, id, debugName);
#endif

        var rbo = new GpuRenderbuffer(id, ownsRenderbuffer: true, debugName);
        rbo.AllocateStorage(storage, width, height, samples);
        return rbo;
    }

    /// <summary>
    /// Creates a new renderbuffer and allocates storage, using a sized internal format enum.
    /// This overload exists for consistency with texture APIs; values are cast to <see cref="RenderbufferStorage"/>.
    /// </summary>
    public static GpuRenderbuffer Create(
        PixelInternalFormat internalFormat,
        int width,
        int height,
        int samples = 0,
        string? debugName = null)
    {
        return Create((RenderbufferStorage)internalFormat, width, height, samples, debugName);
    }

    /// <summary>
    /// Wraps an existing renderbuffer id without taking ownership of deletion.
    /// </summary>
    public static GpuRenderbuffer Wrap(int existingRenderbufferId, string? debugName = null)
    {
        if (existingRenderbufferId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(existingRenderbufferId), existingRenderbufferId, "Renderbuffer id must be > 0.");
        }

#if DEBUG
        GlDebug.TryLabel(ObjectLabelIdentifier.Renderbuffer, existingRenderbufferId, debugName);
#endif

        return new GpuRenderbuffer(existingRenderbufferId, ownsRenderbuffer: false, debugName);
    }

    /// <summary>
    /// Sets the debug label for this renderbuffer (debug builds only).
    /// </summary>
    public void SetDebugName(string? debugName)
    {
        this.debugName = debugName;

#if DEBUG
        if (renderbufferId != 0)
        {
            GlDebug.TryLabel(ObjectLabelIdentifier.Renderbuffer, renderbufferId, debugName);
        }
#endif
    }

    /// <summary>
    /// Binds this renderbuffer to <see cref="RenderbufferTarget.Renderbuffer"/>.
    /// </summary>
    public void Bind()
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuRenderbuffer] Attempted to bind disposed or invalid renderbuffer");
            return;
        }

        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, renderbufferId);
    }

    /// <summary>
    /// Attempts to bind this renderbuffer. Returns <c>false</c> if invalid.
    /// </summary>
    public bool TryBind()
    {
        if (!IsValid)
        {
            return false;
        }

        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, renderbufferId);
        return true;
    }

    /// <summary>
    /// Unbinds any renderbuffer from <see cref="RenderbufferTarget.Renderbuffer"/>.
    /// </summary>
    public void Unbind()
    {
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);
    }

    /// <summary>
    /// Binds this renderbuffer and returns a scope that restores the previous binding when disposed.
    /// </summary>
    public BindingScope BindScope()
    {
        int previous = 0;
        try
        {
            GL.GetInteger(GetPName.RenderbufferBinding, out previous);
        }
        catch
        {
            previous = 0;
        }

        Bind();
        return new BindingScope(previous);
    }

    /// <summary>
    /// Allocates or reallocates renderbuffer storage.
    /// </summary>
    public void AllocateStorage(RenderbufferStorage storage, int width, int height, int samples = 0)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuRenderbuffer] Attempted to allocate storage for disposed or invalid renderbuffer");
            return;
        }

        if (width < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be >= 0.");
        }

        if (height < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be >= 0.");
        }

        if (samples < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(samples), samples, "Samples must be >= 0.");
        }

        using var _ = BindScope();

        if (samples > 0)
        {
            GL.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, samples, storage, width, height);
        }
        else
        {
            GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, storage, width, height);
        }

        this.storage = storage;
        this.width = width;
        this.height = height;
        this.samples = samples;
    }

    /// <summary>
    /// Allocates or reallocates renderbuffer storage, using a sized internal format enum.
    /// This overload exists for consistency with texture APIs; values are cast to <see cref="RenderbufferStorage"/>.
    /// </summary>
    public void AllocateStorage(PixelInternalFormat internalFormat, int width, int height, int samples = 0)
    {
        AllocateStorage((RenderbufferStorage)internalFormat, width, height, samples);
    }

    /// <summary>
    /// Reallocates storage using the previously configured format and sample count.
    /// </summary>
    public void Resize(int width, int height)
    {
        AllocateStorage(storage, width, height, samples);
    }

    /// <summary>
    /// Scope that restores the renderbuffer binding when disposed.
    /// </summary>
    public readonly struct BindingScope : IDisposable
    {
        private readonly int previous;

        public BindingScope(int previous)
        {
            this.previous = previous;
        }

        public void Dispose()
        {
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, previous);
        }
    }
}
