using System;
using System.Collections.Generic;
using System.Threading;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Describes one attachable image and optionally owns the resource allocated for it.</summary>
/// <remarks>Framebuffers borrow this object. Existing-resource factories never transfer ownership.
/// Dispose retires all uses of this image; owned storage is disposed later on the render thread.</remarks>
public sealed class GpuFramebufferAttachment : GpuResource
{
    private GpuResource? resource;
    private readonly bool ownsStorage;
    private readonly GpuResourceDisposalQueue disposalQueue;
    private readonly List<WeakReference<GpuFramebuffer>> framebuffers = new();
    private readonly int externalId;
    private readonly int externalWidth;
    private readonly int externalHeight;
    private readonly int externalSamples;
    private readonly bool renderbuffer;
    private readonly TextureTarget target;
    private readonly PixelInternalFormat format;
    private int retired;

    #region Public API
    #region Construction
    /// <summary>Allocates and owns a resizable two-dimensional texture image.</summary>
    public GpuFramebufferAttachment(int width, int height, PixelInternalFormat format,
        TextureFilterMode filter = TextureFilterMode.Nearest, string? debugName = null)
        : this(CreateTextureStorage(width, height, format, filter, debugName), true, 0, null) { }

    /// <summary>Allocates and owns renderbuffer storage with the requested sample count.</summary>
    public GpuFramebufferAttachment(RenderbufferStorage storage, int width, int height,
        int samples = 0, string? debugName = null)
        : this(CreateRenderbufferStorage(storage, width, height, samples, debugName), true, 0, null) { }

    /// <summary>Borrows an existing texture image; null layer selects the entire mip level.</summary>
    public static GpuFramebufferAttachment FromTexture(GpuTexture texture, int mipLevel = 0, int? layer = null,
        TextureTarget? cubeFace = null)
    {
        ArgumentNullException.ThrowIfNull(texture);
        if (!texture.IsValid) throw new ArgumentException("Texture must be live.", nameof(texture));
        if ((uint)mipLevel >= (uint)texture.StorageMipLevels) throw new ArgumentOutOfRangeException(nameof(mipLevel));
        if (cubeFace.HasValue && (texture.TextureTarget != TextureTarget.TextureCubeMap || !IsCubeFace(cubeFace.Value) || layer.HasValue))
            throw new ArgumentOutOfRangeException(nameof(cubeFace));
        if (layer.HasValue && (texture.TextureTarget is not (TextureTarget.Texture3D or TextureTarget.Texture2DArray)
            || (uint)layer.Value >= (uint)(texture.TextureTarget == TextureTarget.Texture3D
                ? Math.Max(1, texture.Depth >> mipLevel) : texture.Depth)))
            throw new ArgumentOutOfRangeException(nameof(layer));
        return new(texture, false, mipLevel, layer, cubeFace);
    }

    /// <summary>Borrows an existing texture view with selection relative to that view's exposed images.</summary>
    public static GpuFramebufferAttachment FromTexture(GpuTextureView view, int mipLevel = 0, int? layer = null,
        TextureTarget? cubeFace = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (!view.IsValid) throw new ArgumentException("Texture view must be live.", nameof(view));
        var image = FromTextureId(view.TextureId, mipLevel, layer, cubeFace);
        image.resource = view;
        GpuFramebufferAttachmentObservers.Register(view, image);
        return image;
    }

    /// <summary>Borrows an external texture image, capturing validated metadata without creating a texture view.</summary>
    /// <remarks>The external owner must refresh publications after replacing or reallocating storage.</remarks>
    public static GpuFramebufferAttachment FromTextureId(int textureId, int mipLevel = 0, int? layer = null,
        TextureTarget? cubeFace = null)
    {
        if (textureId <= 0 || !GL.IsTexture(textureId)) throw new ArgumentOutOfRangeException(nameof(textureId));
        if ((uint)mipLevel > 30) throw new ArgumentOutOfRangeException(nameof(mipLevel));
        // Query the original object's target and selected image directly, avoiding
        // speculative binds and leaving texture/sampler bindings unchanged.
        GL.GetTextureParameter(textureId, GetTextureParameter.TextureTarget, out int targetValue);
        var textureTarget = (TextureTarget)targetValue;
        if (cubeFace.HasValue && (textureTarget != TextureTarget.TextureCubeMap || !IsCubeFace(cubeFace.Value) || layer.HasValue))
            throw new ArgumentOutOfRangeException(nameof(cubeFace));
        GL.GetTextureLevelParameter(textureId, mipLevel, GetTextureParameter.TextureWidth, out int width);
        GL.GetTextureLevelParameter(textureId, mipLevel, GetTextureParameter.TextureHeight, out int height);
        GL.GetTextureLevelParameter(textureId, mipLevel, GetTextureParameter.TextureDepth, out int depth);
        GL.GetTextureLevelParameter(textureId, mipLevel, GetTextureParameter.TextureInternalFormat, out int format);
        if (width <= 0 || height <= 0) throw new ArgumentException("Texture image has no storage.", nameof(textureId));
        bool layered = textureTarget is TextureTarget.Texture3D or TextureTarget.Texture2DArray
            or TextureTarget.TextureCubeMapArray or TextureTarget.Texture2DMultisampleArray;
        if (layer.HasValue && (!layered || (uint)layer.Value >= (uint)depth)) throw new ArgumentOutOfRangeException(nameof(layer));
        int samples = 0;
        if (textureTarget is TextureTarget.Texture2DMultisample or TextureTarget.Texture2DMultisampleArray)
            GL.GetTextureLevelParameter(textureId, mipLevel, GetTextureParameter.TextureSamples, out samples);
        return new(textureId, false, cubeFace ?? textureTarget, (PixelInternalFormat)format, width, height, samples, mipLevel, layer);
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
    /// <summary>Gets an individual cube face when one was selected.</summary>
    public TextureTarget? CubeFace => IsCubeFace(target) ? target : null;
    /// <summary>Gets the selected image width.</summary>
    public int Width => resource is GpuTexture texture ? Math.Max(1, texture.Width >> MipLevel)
        : resource is GpuRenderbuffer buffer ? buffer.Width : externalWidth;
    /// <summary>Gets the selected image height.</summary>
    public int Height => resource is GpuTexture texture ? Math.Max(1, texture.Height >> MipLevel)
        : resource is GpuRenderbuffer buffer ? buffer.Height : externalHeight;
    /// <summary>Gets the sample count, with zero representing ordinary single-sample storage.</summary>
    public int Samples => resource is GpuRenderbuffer buffer ? buffer.Samples : externalSamples;
    /// <summary>Gets the image format used to validate attachment aspects.</summary>
    public PixelInternalFormat InternalFormat => resource switch
    {
        GpuTexture texture => texture.InternalFormat,
        GpuRenderbuffer buffer => (PixelInternalFormat)buffer.Storage,
        GpuTextureView view => view.InternalFormat,
        _ => format
    };
    /// <summary>Gets the live texture name, or zero for renderbuffer images and retired attachments.</summary>
    public int TextureId => renderbuffer ? 0 : (int)ResourceId;
    /// <summary>Gets the live renderbuffer name, or zero for texture images and retired attachments.</summary>
    public int RenderbufferId => renderbuffer ? (int)ResourceId : 0;
    /// <summary>Gets the natural depth/stencil destination for this image's format.</summary>
    internal FramebufferAttachment DepthStencilSlot => TextureFormatHelper.IsDepthFormat(InternalFormat)
        ? (HasStencil ? FramebufferAttachment.DepthStencilAttachment : FramebufferAttachment.DepthAttachment)
        : FramebufferAttachment.StencilAttachment;

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
        if (MipLevel != 0 || Layer.HasValue) throw new InvalidOperationException("Selected subresources cannot be resized independently.");
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
    /// <summary>Publishes changes to remaining framebuffer references without retaining them strongly.</summary>
    internal void NotifyChanged()
    {
        for (int i = framebuffers.Count - 1; i >= 0; i--)
        {
            if (framebuffers[i].TryGetTarget(out var framebuffer)) framebuffer.OnAttachmentChanged(this);
            else framebuffers.RemoveAt(i);
        }
    }

    /// <summary>Registers a weak observer so externally retained images cannot keep unused framebuffers alive.</summary>
    internal void Observe(GpuFramebuffer framebuffer)
    {
        foreach (var reference in framebuffers)
            if (reference.TryGetTarget(out var existing) && ReferenceEquals(existing, framebuffer)) return;
        framebuffers.Add(new(framebuffer));
    }

    /// <summary>Validates the aspect and attaches this selected image to the currently bound framebuffer.</summary>
    internal void AttachTo(FramebufferAttachment slot)
    {
        ValidateSlot(slot);
        if (renderbuffer)
            GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, slot, RenderbufferTarget.Renderbuffer, RenderbufferId);
        else if (Layer.HasValue)
            GL.FramebufferTextureLayer(FramebufferTarget.Framebuffer, slot, TextureId, MipLevel, Layer.Value);
        else if (target == TextureTarget.Texture2D || target == TextureTarget.TextureRectangle || IsCubeFace(target))
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, slot, target, TextureId, MipLevel);
        else
            GL.FramebufferTexture(FramebufferTarget.Framebuffer, slot, TextureId, MipLevel);
    }

    /// <summary>Rejects retired storage and incompatible color, depth, or stencil attachment points.</summary>
    internal void ValidateSlot(FramebufferAttachment slot)
    {
        if (!IsValid) throw new InvalidOperationException("Framebuffer attachment storage has retired.");
        bool depth = TextureFormatHelper.IsDepthFormat(InternalFormat);
        bool stencil = HasStencil;
        bool compatible = slot switch
        {
            FramebufferAttachment.DepthAttachment => depth,
            FramebufferAttachment.StencilAttachment => stencil,
            FramebufferAttachment.DepthStencilAttachment => depth && stencil,
            _ => (int)slot >= (int)FramebufferAttachment.ColorAttachment0
                && (int)slot <= (int)FramebufferAttachment.ColorAttachment15 && !depth && !stencil
        };
        if (!compatible) throw new ArgumentException("Storage format does not match the attachment point.", nameof(slot));
    }


    /// <summary>Builds a borrowed image discovered from an external framebuffer without claiming storage ownership.</summary>
    internal static GpuFramebufferAttachment FromExternalImage(int id, bool renderbuffer, TextureTarget target,
        PixelInternalFormat format, int width, int height, int samples, int mipLevel, int? layer)
        => new(id, renderbuffer, target, format, width, height, samples, mipLevel, layer);
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
        NotifyChanged();
        framebuffers.Clear();
    }
    #endregion

    #region Private
    /// <summary>Allocates owned texture storage only while its disposal lifetime can accept cleanup.</summary>
    private static DynamicTexture2D CreateTextureStorage(int width, int height, PixelInternalFormat format,
        TextureFilterMode filter, string? debugName)
    {
        if (GpuResourceManagerSystem.CaptureDisposalQueue().IsClosed)
            throw new InvalidOperationException("Initialize the GPU resource manager before creating new owned attachments after shutdown.");
        return DynamicTexture2D.Create(width, height, format, filter, debugName);
    }

    /// <summary>Allocates owned renderbuffer storage only while deferred cleanup remains available.</summary>
    private static GpuRenderbuffer CreateRenderbufferStorage(RenderbufferStorage storage, int width, int height,
        int samples, string? debugName)
    {
        if (GpuResourceManagerSystem.CaptureDisposalQueue().IsClosed)
            throw new InvalidOperationException("Initialize the GPU resource manager before creating new owned attachments after shutdown.");
        return GpuRenderbuffer.Create(storage, width, height, samples, debugName);
    }

    /// <summary>Includes packed formats as well as stencil-only storage in stencil aspect validation.</summary>
    private bool HasStencil => TextureFormatHelper.IsStencilFormat(InternalFormat)
        || InternalFormat is PixelInternalFormat.Depth24Stencil8 or PixelInternalFormat.Depth32fStencil8;
    /// <summary>Captures one managed backing resource and its selected image without changing its lifecycle.</summary>
    private GpuFramebufferAttachment(GpuResource resource, bool ownsStorage, int mipLevel, int? layer, TextureTarget? cubeFace = null)
    {
        this.resource = resource;
        this.ownsStorage = ownsStorage;
        disposalQueue = GpuResourceManagerSystem.CaptureDisposalQueue();
        MipLevel = mipLevel;
        Layer = layer;
        renderbuffer = resource is GpuRenderbuffer;
        target = cubeFace ?? (resource is GpuTexture texture ? texture.TextureTarget : 0);
        format = resource is GpuTexture image ? image.InternalFormat : (PixelInternalFormat)((GpuRenderbuffer)resource).Storage;
        GpuFramebufferAttachmentObservers.Register(resource, this);
        if (!ownsStorage) GC.SuppressFinalize(this);
    }

    /// <summary>Retains imported image metadata without creating another GL object.</summary>
    private GpuFramebufferAttachment(int id, bool renderbuffer, TextureTarget target, PixelInternalFormat format,
        int width, int height, int samples, int mipLevel, int? layer)
    {
        externalId = id;
        this.renderbuffer = renderbuffer;
        this.target = target;
        this.format = format;
        externalWidth = width;
        externalHeight = height;
        externalSamples = samples;
        MipLevel = mipLevel;
        Layer = layer;
        disposalQueue = GpuResourceManagerSystem.CaptureDisposalQueue();
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
    private static bool IsCubeFace(TextureTarget value) => value >= TextureTarget.TextureCubeMapPositiveX
        && value <= TextureTarget.TextureCubeMapNegativeZ;
    #endregion
}
