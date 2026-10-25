using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an OpenGL renderbuffer object (RBO).
/// Deletion is deferred to <see cref="GpuResourceManager"/> when available.
/// All methods require a current GL context on the calling thread.
/// </summary>
public sealed class GpuRenderbuffer : GpuResource<GpuRenderbufferMetadata>, IDisposable
{
    /// <summary>Supplies the manager-published descriptor for query-free selected-image projection.</summary>
    internal GpuRenderbufferMetadata MetadataSnapshot => metadata;

    private int renderbufferId;
    private readonly bool ownsRenderbuffer;
    private string? debugName;

    /// <summary>Accesses the canonical renderbuffer storage snapshot.</summary>
    private RenderbufferStorage storage => (RenderbufferStorage)metadata.Format;
    /// <summary>Accesses the canonical renderbuffer width snapshot.</summary>
    private int width => metadata.Width;
    /// <summary>Accesses the canonical renderbuffer height snapshot.</summary>
    private int height => metadata.Height;
    /// <summary>Accesses the canonical renderbuffer samples snapshot.</summary>
    private int samples => metadata.Samples;

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

    /// <inheritdoc />
    public override nint ResourceId
    {
        get => renderbufferId;
        protected set => renderbufferId = (int)value;
    }

    protected override GpuResourceKind ResourceKind => GpuResourceKind.Renderbuffer;

    protected override bool OwnsResource => ownsRenderbuffer;

    /// <summary>
    /// Returns <c>true</c> when the renderbuffer has a non-zero id and has not been disposed.
    /// </summary>
    public new bool IsValid => base.IsValid;

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
        var request = new GpuRenderbufferInitialization { Format = storage, Width = width, Height = height, Samples = samples };
        var resource = new GpuRenderbuffer(0, ownsRenderbuffer: true, debugName);
        if (!GpuResourceManager.CreateRenderbuffer(request, resource, out _).HasValue)
            throw new InvalidOperationException("Renderbuffer creation failed.");
        resource.SetDebugName(debugName);
        return resource;
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

        var wrapper = new GpuRenderbuffer(existingRenderbufferId, ownsRenderbuffer: false, debugName);
        GpuResourceManager.AcquireWrapperMetadata(wrapper, GpuRenderbufferMetadataReader.Instance);
        return wrapper;
    }

    /// <summary>
    /// Sets the debug label for this renderbuffer (debug builds only).
    /// </summary>
    public override void SetDebugName(string? debugName)
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

        StateCache.Current.BindRenderbuffer(renderbufferId);
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

        StateCache.Current.BindRenderbuffer(renderbufferId);
        return true;
    }

    /// <summary>
    /// Unbinds any renderbuffer from <see cref="RenderbufferTarget.Renderbuffer"/>.
    /// </summary>
    public void Unbind()
    {
        StateCache.Current.BindRenderbuffer(0);
    }

    /// <summary>
    /// Binds this renderbuffer and returns a scope that restores the previous binding when disposed.
    /// </summary>
    public BindingScope BindScope()
    {
        ObjectDisposedException.ThrowIf(!IsValid, this);
        var scope = StateCache.Current.BindRenderbufferScope(renderbufferId);
        return new BindingScope(scope);
    }

    /// <summary>
    /// Allocates or reallocates renderbuffer storage.
    /// </summary>
    public void AllocateStorage(RenderbufferStorage storage, int width, int height, int samples = 0)
    {
        RequireMutableStorage();
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

        StateCache.Current.RenderbufferStorage(this, storage, width, height, samples);
        if (ResourceManager?.GetResourceRegistration(this) is null)
            GpuFramebufferAttachmentObservers.NotifyChanged(this);
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
    /// <returns>True if storage was resized; false for unchanged dimensions or a retired renderbuffer.</returns>
    public bool Resize(int width, int height)
    {
        if (!IsValid || (this.width == width && this.height == height)) return false;
        AllocateStorage(storage, width, height, samples);
        return true;
    }

    /// <summary>
    /// Scope that restores the renderbuffer binding when disposed.
    /// </summary>
    public readonly struct BindingScope : IDisposable
    {
        private readonly StateCache.RenderbufferScope scope;
        private readonly int previous;
        private readonly bool useCacheScope;

        internal BindingScope(StateCache.RenderbufferScope scope)
        {
            this.scope = scope;
            previous = 0;
            useCacheScope = true;
        }

        public BindingScope(int previous)
        {
            scope = default;
            this.previous = previous;
            useCacheScope = false;
        }

        public void Dispose()
        {
            if (useCacheScope)
            {
                scope.Dispose();
                return;
            }

            StateCache.Current.BindRenderbuffer(previous);
        }
    }
}
