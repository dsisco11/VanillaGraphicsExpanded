using System;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Diagnostics;


namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an OpenGL renderbuffer object (RBO).
/// Deletion is deferred to <see cref="RHIResourceManager"/> when available.
/// All methods require a current GL context on the calling thread.
/// </summary>
public sealed class GpuRenderbuffer : GpuResource<GpuRenderbufferMetadata>, IDisposable
{
    /// <summary>Supplies the manager-published descriptor for query-free selected-image projection.</summary>
    internal GpuRenderbufferMetadata MetadataSnapshot => metadata;

    private int renderbufferId;

    /// <summary>Supplies a query-free consumer reference without lifecycle authority.</summary>
    public static implicit operator GpuRenderbufferRef(GpuRenderbuffer? value) => new(value);
    private readonly bool ownsRenderbuffer;
    private string? debugName;

    /// <summary>Accesses the canonical renderbuffer storage snapshot.</summary>
    private TextureFormat storage => metadata.Format;
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
    public TextureFormat Storage => storage;

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

    /// <summary>Declares an unpublished owner or explicitly borrowed native identity for manager handoff.</summary>
    internal GpuRenderbuffer(int renderbufferId, bool ownsRenderbuffer, string? debugName)
    {
        this.renderbufferId = renderbufferId;
        this.ownsRenderbuffer = ownsRenderbuffer;
        this.debugName = debugName;
    }

    /// <summary>
    /// Sets the debug label for this renderbuffer (debug builds only).
    /// </summary>
    public override void SetDebugName(string? debugName)
    {
        this.debugName = debugName;

        RHICommandContext.TryLabelResource(this, debugName);
    }


    /// <summary>
    /// Allocates or reallocates renderbuffer storage.
    /// </summary>
    public void AllocateStorage(TextureFormat storage, int width, int height, int samples = 0)
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

        RHICommandContext.Current.AllocateRenderbufferStorage(this, new RHIRenderbufferDescription { Format = storage, Width = width, Height = height, Samples = samples });
    }

    /// <summary>
    /// Reallocates storage using the previously configured format and sample count.
    /// </summary>
    /// <returns>True if storage was resized; false for unchanged dimensions or a retired renderbuffer.</returns>
    public bool Resize(int width, int height)
    {
        if (!IsValid || (this.width == width && this.height == height)) return false;
        RHICommandContext.Current.ResizeRenderbuffer(this, width, height);
        return true;
    }


}
