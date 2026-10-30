using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Collections.Generic;
using System.Threading;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Describes one attachable image and optionally owns the resource allocated for it.</summary>
/// <remarks>Framebuffers borrow this object. Existing-resource factories never transfer ownership.
/// Dispose retires all uses of this image; owned storage is disposed later on the render thread.</remarks>
public sealed class GpuFramebufferAttachment : GpuResource<GpuImageMetadata>
{
    private GpuResource? resource;
    private readonly bool ownsStorage;
    private readonly RHIResourceDisposalQueue disposalQueue;
    private readonly List<WeakReference<GpuFramebuffer>> framebuffers = new();
    private readonly int externalId;
    private readonly bool renderbuffer;
    private readonly TextureDimension target;
    private readonly CubeFace? cubeFace;
    private readonly GpuFramebufferAttachmentMode attachmentMode;

    private int retired;

    /// <summary>Invalidates weak framebuffer consumers after projection and backing changes commit.</summary>
    internal override void OnResourceChanged(in RHIResourceChange change) => NotifyDependents();

    #region Public API
    #region Construction
    /// <summary>Borrows a view-relative multiview range without transferring storage ownership.</summary>
    public static GpuFramebufferAttachment FromMultiviewTexture(GpuTextureRef texture, int baseView, int viewCount, int mipLevel = 0)
    {
        if (!texture.IsValid || texture.Target != TextureDimension.TwoArray)
            throw new ArgumentException("Multiview attachments require a live two-dimensional array texture.", nameof(texture));
        texture.RequireImage(mipLevel);
        return new(texture.Resource!, false, mipLevel, null, null, baseView, viewCount, GpuFramebufferAttachmentMode.Multiview);
    }
    /// <summary>Allocates and owns a resizable two-dimensional texture image.</summary>
    public GpuFramebufferAttachment(int width, int height, TextureFormat format,
        TextureFilterMode filter = TextureFilterMode.Nearest, string? debugName = null)
        : this(CreateTextureStorage(width, height, format, filter, debugName), true, 0, null) { }

    /// <summary>Allocates and owns renderbuffer storage with the requested sample count.</summary>
    public GpuFramebufferAttachment(TextureFormat storage, int width, int height,
        int samples = 0, string? debugName = null)
        : this(CreateRenderbufferStorage(storage, width, height, samples, debugName), true, 0, null) { }

    /// <summary>Borrows a published texture image with selection relative to the referenced object.</summary>
    public static GpuFramebufferAttachment FromTexture(GpuTextureRef texture, int mipLevel = 0, int? layer = null,
        CubeFace? cubeFace = null)
    {
        if (!texture.IsValid) throw new ArgumentException("Texture must be usable.", nameof(texture));
        GpuTextureLevelMetadata selected;
        try { selected = texture.RequireImage(mipLevel); }
        catch (InvalidOperationException) { throw new ArgumentOutOfRangeException(nameof(mipLevel)); }
        if (cubeFace.HasValue && (texture.Target != TextureDimension.Cube || !IsCubeFace(cubeFace.Value) || layer.HasValue))
            throw new ArgumentOutOfRangeException(nameof(cubeFace));
        int layers = texture.Target == TextureDimension.OneArray ? selected.Height : selected.Depth;
        if (layer.HasValue && (texture.Target is not (TextureDimension.Three or TextureDimension.TwoArray
                or TextureDimension.OneArray or TextureDimension.CubeArray or TextureDimension.TwoMultisampleArray)
            || (uint)layer.Value >= (uint)layers)) throw new ArgumentOutOfRangeException(nameof(layer));
        return new(texture.Resource!, false, mipLevel, layer, cubeFace);
    }
    /// <summary>Borrows an external texture image, capturing validated metadata without creating a texture view.</summary>
    /// <remarks>The external owner must refresh publications after replacing or reallocating storage.</remarks>
    public static GpuFramebufferAttachment FromTextureId(int textureId, int mipLevel = 0, int? layer = null,
        CubeFace? cubeFace = null)
    {
        if ((uint)mipLevel > 30) throw new ArgumentOutOfRangeException(nameof(mipLevel));
        var data = RHICommandContext.Current.AcquireExternalAttachmentTexture(textureId);
        if (cubeFace.HasValue && (data.Target != TextureDimension.Cube || !IsCubeFace(cubeFace.Value) || layer.HasValue))
            throw new ArgumentOutOfRangeException(nameof(cubeFace));
        var image = new GpuFramebufferAttachment(textureId, false, data.Target, mipLevel, layer, data, cubeFace: cubeFace);
        if (image.Width <= 0 || image.Height <= 0 || (image.ResourceManager is { } manager
            && manager.GetResourceRegistration(image) is not null && !manager.IsResourceMetadataAvailable(image)))
            throw new ArgumentException("The selected image has no compatible allocated storage.");
        return image;
    }

    /// <summary>Borrows an existing renderbuffer without assuming responsibility for disposal.</summary>
    public static GpuFramebufferAttachment FromRenderbuffer(GpuRenderbuffer renderbuffer)
    {
        ArgumentNullException.ThrowIfNull(renderbuffer);
        if (!renderbuffer.IsValid) throw new ArgumentException("Renderbuffer must be live.", nameof(renderbuffer));
        return new(renderbuffer, false, 0, null);
    }
    #endregion

    #region Image and lifetime
    /// <summary>Resolves the existing object's name without ever acquiring ownership of that name.</summary>
    public override nint ResourceId
    {
        get => Volatile.Read(ref retired) != 0 ? 0
            : resource is GpuTextureView view ? (view.IsValid ? view.ResourceId : 0)
            : resource is { } storage ? (storage.IsValid ? storage.ResourceId : 0) : externalId;
        protected set => Interlocked.Exchange(ref retired, 1);
    }
    /// <summary>Gets the existing storage wrapper, when this image has a managed backing resource.</summary>
    public GpuResource? Resource => resource;
    /// <summary>Reports whether this attachment allocated its backing storage.</summary>
    public bool OwnsStorage => ownsStorage;
    /// <summary>Gets the selected texture mip level.</summary>
    public int MipLevel { get; }
    /// <summary>Gets the selected layer, distinguishing layer zero from an entire layered level.</summary>
    public int? Layer { get; }
    /// <summary>Gets the first selected view in an explicit multiview range.</summary>
    public int BaseView { get; }
    /// <summary>Gets the selected view count, or zero for an ordinary image selection.</summary>
    public int ViewCount { get; }
    /// <summary>Gets proven command semantics separately from native range queries.</summary>
    internal GpuFramebufferAttachmentMode AttachmentMode => attachmentMode;
    /// <summary>Gets an individual cube face when one was selected.</summary>
    public CubeFace? CubeFace => cubeFace;
    /// <summary>Gets the selected image width.</summary>
    public int Width => metadata.Width;
    /// <summary>Gets the selected layered image extent without copying a native storage descriptor.</summary>
    internal int ImageDepth => metadata.Depth;
    /// <summary>Gets the attachment target chosen for this image relationship.</summary>
    internal TextureDimension ImageTarget => target;
    /// <summary>Gets the selected image height.</summary>
    public int Height => metadata.Height;
    /// <summary>Gets the sample count, with zero representing ordinary single-sample storage.</summary>
    public int Samples => metadata.Samples;
    /// <summary>Gets the image format used to validate attachment aspects.</summary>
    public TextureFormat InternalFormat => metadata.Format;
    /// <summary>Projects the published image format for semantic pipeline inputs without native discovery.</summary>
    public TextureFormat Format => metadata.Format;
    /// <summary>Gets the live texture name, or zero for renderbuffer images and retired attachments.</summary>
    public int TextureId => renderbuffer ? 0 : (int)ResourceId;
    /// <summary>Gets the live renderbuffer name, or zero for texture images and retired attachments.</summary>
    public int RenderbufferId => renderbuffer ? (int)ResourceId : 0;
    /// <summary>Gets the natural depth/stencil destination for this image's format.</summary>
    internal FramebufferSlot DepthStencilSlot => TextureFormatHelper.IsDepthFormat(InternalFormat)
        ? (HasStencil ? FramebufferSlot.DepthStencil : FramebufferSlot.Depth)
        : FramebufferSlot.Stencil;

    /// <summary>Labels the image's backing resource when it was created by this attachment.</summary>
    public override void SetDebugName(string? debugName)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (ownsStorage) resource?.SetDebugName(debugName);
    }

    /// <summary>Rejects handle transfer because the attachment is an image relationship, not a second handle owner.</summary>
    public override nint Detach() => throw new NotSupportedException("Detach the backing resource through its owner.");

    /// <summary>Resizes a whole resizable image and notifies every framebuffer sharing this attachment.</summary>
    public bool Resize(int width, int height)
    {
        if (!IsValid) return false;
        if (MipLevel != 0 || Layer.HasValue || ViewCount > 0) throw new InvalidOperationException("Selected subresources cannot be resized independently.");
        bool changed = resource switch
        {
            DynamicTexture2D texture => texture.Resize(width, height),
            GpuRenderbuffer buffer => buffer.Resize(width, height),
            _ => throw new InvalidOperationException("This image does not expose resizable storage.")
        };
        return changed;
    }
    #endregion
    #endregion

    #region Internal API
    /// <summary>Compares image selections without inferring multiview mode from an ordinary positive native count.</summary>
    internal bool MatchesNativeSelection(GpuFramebufferImageMetadata data, bool retainAuthoredMode = false)
    {
        if (MipLevel != data.MipLevel || (CubeFace ?? 0) != data.CubeFace) return false;
        if (target == TextureDimension.TwoArray)
        {
            // Context-local tools have no persistent observation subscription. Retain an
            // authored image relationship when queried selection facts still match it;
            // absence of a tracked mode must not discard managed storage ownership.
            var mode = data.Mode == GpuFramebufferAttachmentMode.Unknown && retainAuthoredMode ? attachmentMode : data.Mode;
            if (attachmentMode != mode) return false;
            if (mode == GpuFramebufferAttachmentMode.Multiview
                || mode == GpuFramebufferAttachmentMode.Unknown && data.Layered && data.ViewCount > 0)
                return !Layer.HasValue && BaseView == data.BaseView && ViewCount == data.ViewCount;
        }
        return MetadataImageSelection.Layer == data.Layer && MetadataImageSelection.Layered == data.Layered;
    }

    /// <summary>Gets this image's mip, layer or cube-face choice independently of generic resource metadata.</summary>
    internal GpuImageSelection MetadataImageSelection => new(MipLevel, Layer ?? 0, CubeFace ?? 0,
        !Layer.HasValue && !CubeFace.HasValue && target is TextureDimension.Cube or TextureDimension.OneArray or TextureDimension.TwoArray
            or TextureDimension.Three or TextureDimension.CubeArray or TextureDimension.TwoMultisampleArray,
        BaseView, ViewCount);

    /// <summary>Invalidates every retained framebuffer after manager publication or failed image acquisition.</summary>
    internal void NotifyDependents()
    {
        List<Exception>? failures = null;
        // Snapshot callbacks may change subscriptions. One failed consumer must not prevent
        // the remaining consumers from withdrawing their previous configuration.
        foreach (var reference in framebuffers.ToArray())
        {
            if (reference.TryGetTarget(out var framebuffer))
            {
                try { framebuffer.OnAttachmentChanged(this); }
                catch (Exception error) { (failures ??= new()).Add(error); }
            }
            else framebuffers.Remove(reference);
        }
        if (failures is not null) throw new AggregateException("Framebuffer dependency notification failed.", failures);
    }

    /// <summary>Registers a weak observer so externally retained images cannot keep unused framebuffers alive.</summary>
    internal void Observe(GpuFramebuffer framebuffer)
    {
        foreach (var reference in framebuffers)
            if (reference.TryGetTarget(out var existing) && ReferenceEquals(existing, framebuffer)) return;
        framebuffers.Add(new(framebuffer));
    }

    /// <summary>Submits this selected image through the framebuffer owner's execution contract.</summary>
    internal void AttachTo(GpuFramebuffer framebuffer, FramebufferSlot slot)
    {
        ValidateSlot(slot);
        RHICommandContext.Current.ConfigureFramebufferAttachment(framebuffer, slot, this);
    }

    /// <summary>Rejects retired storage and incompatible color, depth, or stencil attachment points.</summary>
    internal void ValidateSlot(FramebufferSlot slot)
    {
        if (!IsValid) throw new InvalidOperationException("Framebuffer attachment storage has retired.");
        if (Width <= 0 || Height <= 0)
            throw new InvalidOperationException("The selected framebuffer image metadata is unavailable.");
        if (ResourceManager is { } manager && manager.GetResourceRegistration(this) is not null
            && !manager.IsResourceMetadataAvailable(this))
            throw new InvalidOperationException("The selected framebuffer image metadata is unavailable.");
        bool depth = TextureFormatHelper.IsDepthFormat(InternalFormat);
        bool stencil = HasStencil;
        bool compatible = slot switch
        {
            FramebufferSlot.Depth => depth,
            FramebufferSlot.Stencil => stencil,
            FramebufferSlot.DepthStencil => depth && stencil,
            _ => (int)slot >= (int)FramebufferSlot.Color0
                && (int)slot <= (int)FramebufferSlot.Color15
                && (int)slot - (int)FramebufferSlot.Color0 < RHICommandContext.Current.Capabilities.MaxColorAttachments
                && !depth && !stencil
        };
        if (!compatible) throw new ArgumentException("Storage format does not match the attachment point.", nameof(slot));
    }


    /// <summary>Projects a retained renderbuffer consumer using only its existing manager-certified snapshot.</summary>
    internal static GpuFramebufferAttachment FromRenderbufferReference(GpuRenderbufferRef reference)
    {
        if (!reference.IsValid) throw new InvalidOperationException("Renderbuffer metadata is unavailable.");
        return new(reference.RequireResource(), false, 0, null);
    }

    /// <summary>Builds a borrowed image discovered from an external framebuffer without claiming storage ownership.</summary>
    internal static GpuFramebufferAttachment FromExternalImage(int id, bool renderbuffer, TextureDimension target,
        int mipLevel, int? layer, GpuTextureMetadata? acquiredTexture = null, int baseView = 0, int viewCount = 0,
        GpuFramebufferAttachmentMode mode = GpuFramebufferAttachmentMode.Ordinary, GpuRenderbufferMetadata? acquiredRenderbuffer = null, CubeFace? cubeFace = null)
        => new(id, renderbuffer, target, mipLevel, layer, acquiredTexture, baseView, viewCount, mode, acquiredRenderbuffer, cubeFace);
    #endregion

    #region Protected API
    /// <summary>Identifies the backing GL object type; this wrapper never deletes its name directly.</summary>
    protected override GpuResourceKind ResourceKind => renderbuffer ? GpuResourceKind.Renderbuffer : GpuResourceKind.Texture;
    /// <summary>Prevents base disposal from independently deleting the backing handle.</summary>
    protected override bool OwnsResource => false;

    /// <summary>Queues owned storage exactly once after explicit retirement and notifies live consumers.</summary>
    protected override void OnAfterDelete()
    {
        ReleaseStorage();
        GC.SuppressFinalize(this);
        NotifyDependents();
        framebuffers.Clear();
    }
    #endregion

    #region Private
    /// <summary>Allocates owned texture storage only while its disposal lifetime can accept cleanup.</summary>
    private static DynamicTexture2D CreateTextureStorage(int width, int height, TextureFormat format,
        TextureFilterMode filter, string? debugName)
    {
        if (RHIResourceManagerSystem.CaptureDisposalQueue().IsClosed)
            throw new InvalidOperationException("Initialize the GPU resource manager before creating new owned attachments after shutdown.");
        var owner = new DynamicTexture2D();
        try
        {
            RHICommandContext.Current.AllocateTextureOwner(owner, new GpuTextureInitialization { Target = TextureDimension.Two,
                InternalFormat = format, Width = Math.Max(1, width), Height = Math.Max(1, height), Depth = 1,
                Levels = 1, BaseLevel = 0, MaxLevel = 0, AllocationMode = GpuTextureAllocationMode.Mutable }, filter, debugName);
            return owner;
        }
        catch { try { owner.Dispose(); } catch { } throw; }
    }

    /// <summary>Allocates owned renderbuffer storage only while deferred cleanup remains available.</summary>
    private static GpuRenderbuffer CreateRenderbufferStorage(TextureFormat storage, int width, int height,
        int samples, string? debugName)
    {
        if (RHIResourceManagerSystem.CaptureDisposalQueue().IsClosed)
            throw new InvalidOperationException("Initialize the GPU resource manager before creating new owned attachments after shutdown.");
        var owner = new GpuRenderbuffer(0, true, debugName);
        try
        {
            RHICommandContext.Current.AllocateRenderbufferOwner(owner,
                new() { Format = storage, Width = width, Height = height, Samples = samples });
            return owner;
        }
        catch { try { owner.Dispose(); } catch { } throw; }
    }

    /// <summary>Includes packed formats as well as stencil-only storage in stencil aspect validation.</summary>
    private bool HasStencil => TextureFormatHelper.IsStencilFormat(InternalFormat)
        || InternalFormat is TextureFormat.Depth24Stencil8 or TextureFormat.Depth32fStencil8;
    /// <summary>Captures one managed backing resource and its selected image without changing its lifecycle.</summary>
    private GpuFramebufferAttachment(GpuResource resource, bool ownsStorage, int mipLevel, int? layer, CubeFace? cubeFace = null,
        int baseView = 0, int viewCount = 0, GpuFramebufferAttachmentMode mode = GpuFramebufferAttachmentMode.Ordinary)
    {
        this.resource = resource;
        this.ownsStorage = ownsStorage;
        disposalQueue = RHIResourceManagerSystem.CaptureDisposalQueue();
        MipLevel = mipLevel;
        this.cubeFace = cubeFace;
        Layer = layer;
        BaseView = baseView;
        ViewCount = viewCount;
        attachmentMode = mode;
        renderbuffer = resource is GpuResource<GpuRenderbufferMetadata>;
        target = (resource is GpuResource<GpuTextureMetadata> texture
            ? RHIResourceManager.RequireTextureReferenceMetadata(texture).Target : 0);
        try
        {
            ValidateSelection();
            RHICommandContext.AcquireAttachmentImage(this);
            RHICommandContext.Current.ObserveAttachmentOwner(resource, this);
            if (!ownsStorage) GC.SuppressFinalize(this);
        }
        catch
        {
            // Construction has not transferred the storage to a usable attachment.
            // Return only owned candidates to their captured cleanup lifetime.
            ReleaseStorage();
            GC.SuppressFinalize(this);
            throw;
        }
    }

    /// <summary>Retains an imported image selection while the manager acquires its native snapshot.</summary>
    private GpuFramebufferAttachment(int id, bool renderbuffer, TextureDimension target, int mipLevel, int? layer, GpuTextureMetadata? acquiredTexture = null,
        int baseView = 0, int viewCount = 0, GpuFramebufferAttachmentMode mode = GpuFramebufferAttachmentMode.Ordinary, GpuRenderbufferMetadata? acquiredRenderbuffer = null, CubeFace? cubeFace = null)
    {
        externalId = id;
        this.renderbuffer = renderbuffer;
        this.target = target;
        this.cubeFace = cubeFace;
        MipLevel = mipLevel;
        Layer = layer;
        BaseView = baseView;
        ViewCount = viewCount;
        attachmentMode = mode;
        disposalQueue = RHIResourceManagerSystem.CaptureDisposalQueue();
        ValidateSelection();
        RHICommandContext.AcquireAttachmentImage(this, acquiredTexture, acquiredRenderbuffer);
        GC.SuppressFinalize(this);
    }

    /// <summary>Enqueues unreachable owned storage without callbacks or GL work on the finalizer thread.</summary>
    ~GpuFramebufferAttachment() => ReleaseStorage();

    /// <summary>Atomically relinquishes the owned wrapper to the queue shared by both cleanup entry points.</summary>
    private void ReleaseStorage()
    {
        var previous = Interlocked.Exchange(ref resource, null);
        if (ownsStorage && previous is not null) disposalQueue.Enqueue(previous);
    }

    /// <summary>Identifies individual cube faces accepted by the two-dimensional attachment entry point.</summary>
    private static bool IsCubeFace(CubeFace value) => value >= RHI.CubeFace.PositiveX
        && value <= RHI.CubeFace.NegativeZ;

    /// <summary>Rejects selections the chosen image target cannot express before acquiring a projection.</summary>
    private void ValidateSelection()
    {
        if ((uint)MipLevel > 30) throw new ArgumentOutOfRangeException(nameof(MipLevel));
        if ((ViewCount != 0 || attachmentMode == GpuFramebufferAttachmentMode.Multiview)
            && (target != TextureDimension.TwoArray || Layer.HasValue || BaseView < 0 || ViewCount < 1))
            throw new ArgumentOutOfRangeException(nameof(ViewCount));
        if (Layer.HasValue && (Layer.Value < 0 || target is not (TextureDimension.OneArray
            or TextureDimension.TwoArray or TextureDimension.Three or TextureDimension.CubeArray
            or TextureDimension.TwoMultisampleArray))) throw new ArgumentOutOfRangeException(nameof(Layer));
    }
    #endregion
}
