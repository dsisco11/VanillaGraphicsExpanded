using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns a framebuffer handle and borrows independently managed attachment images.</summary>
public abstract partial class GpuFramebuffer : GpuResource<GpuFramebufferMetadata>
{
    private int fboId;
    private int wrappedWidth;
    private int wrappedHeight;
    private readonly bool ownsFramebuffer;
    private readonly Dictionary<FramebufferSlot, GpuFramebufferAttachment> attachments = new();
    private string? debugName;
    private bool attachmentsDirty;
    private bool resizing;

    #region Public API
    #region Properties
    /// <summary>Notifies dependents after image changes, external refresh, or retirement.</summary>
    public event Action? AttachmentsChanged;
    /// <summary>Exposes the framebuffer handle to the common resource lifecycle.</summary>
    public override nint ResourceId { get => fboId; protected set => fboId = (int)value; }
    /// <summary>Gets the framebuffer name, or zero after retirement.</summary>
    public int FboId => fboId;
    /// <summary>Identifies native ownership for explicit external-surface admission.</summary>
    internal bool OwnsNativeFramebuffer => ownsFramebuffer;
    /// <summary>Exposes only externally owned framebuffer identity to pass consumers.</summary>
    internal RHI.RHISurfaceRef SurfaceReference => new(this);
    /// <summary>Gets the number of occupied color slots, excluding sparse holes.</summary>
    public int ColorAttachmentCount => metadata.Attachments?.Keys.Count(IsColorSlot) ?? attachments.Keys.Count(IsColorSlot);
    /// <summary>Reports whether a depth image is configured.</summary>
    public bool HasDepthAttachment => metadata.Attachments?.ContainsKey(FramebufferSlot.Depth) ?? attachments.ContainsKey(FramebufferSlot.Depth);
    /// <summary>Gets the diagnostic name.</summary>
    public string? DebugName => debugName;
    /// <summary>Gets the first configured image width or external viewport width.</summary>
    public int Width => FirstAttachment?.Width ?? (metadata.DefaultWidth > 0 ? metadata.DefaultWidth : wrappedWidth);
    /// <summary>Gets the first configured image height or external viewport height.</summary>
    public int Height => FirstAttachment?.Height ?? (metadata.DefaultHeight > 0 ? metadata.DefaultHeight : wrappedHeight);
    /// <summary>Gets an existing dynamic texture at the specified color slot for compatibility.</summary>
    public DynamicTexture2D this[int index] => GetAttachment(ColorSlot(index))?.Resource as DynamicTexture2D
        ?? throw new InvalidOperationException("The color slot does not contain a dynamic 2D texture.");
    /// <summary>Gets a managed dynamic depth texture when present.</summary>
    public DynamicTexture2D? DepthTexture => GetAttachment(FramebufferSlot.Depth)?.Resource as DynamicTexture2D;
    /// <summary>Gets a managed depth renderbuffer when present.</summary>
    public GpuRenderbuffer? DepthRenderbuffer => GetAttachment(FramebufferSlot.Depth)?.Resource as GpuRenderbuffer;
    #endregion

    #region Attachments
    /// <summary>Gets an exact slot; packed depth/stencil requires both aspects to share the same image.</summary>
    public GpuFramebufferAttachment? GetAttachment(FramebufferSlot slot)
    {
        ReconcileAttachmentMetadata();
        if (slot == FramebufferSlot.DepthStencil)
        {
            var depth = GetAttachment(FramebufferSlot.Depth);
            return ReferenceEquals(depth, GetAttachment(FramebufferSlot.Stencil)) ? depth : null;
        }
        return attachments.GetValueOrDefault(slot);
    }

    /// <summary>Configures an image while preserving bindings and retaining a non-owning strong reference.</summary>
    public abstract void SetAttachment(FramebufferSlot slot, GpuFramebufferAttachment attachment);

    /// <summary>Detaches images and releases references without disposing shared attachment instances.</summary>
    public abstract bool RemoveAttachment(FramebufferSlot slot);

    /// <summary>Gets a live color texture name, or zero for absent and renderbuffer-backed slots.</summary>
    public int GetColorTextureId(int index) => IsDisposed ? 0 : GetAttachment(ColorSlot(index))?.TextureId ?? 0;
    /// <summary>Gets the live depth texture name when available.</summary>
    public int GetDepthTextureId() => IsDisposed ? 0 : GetAttachment(FramebufferSlot.Depth)?.TextureId ?? 0;
    /// <summary>Gets the live depth renderbuffer name when available.</summary>
    public int GetDepthRenderbufferId() => IsDisposed ? 0 : GetAttachment(FramebufferSlot.Depth)?.RenderbufferId ?? 0;

    /// <summary>Resizes distinct whole backing resources and publishes one completed update for this framebuffer.</summary>
    public abstract bool Resize(int newWidth, int newHeight);

    #endregion

    #region Rendering
    /// <summary>Binds this framebuffer after rejecting retired images and refreshing changed image bindings.</summary>
    public void Bind() => Bind(FramebufferSelection.ReadAndDraw);

    /// <summary>Refreshes changed images on the draw binding, optionally selecting the read alias for compatibility.</summary>
    internal void Bind(FramebufferSelection bindingTarget)
    {
        if (bindingTarget is not (FramebufferSelection.ReadAndDraw or FramebufferSelection.Draw))
            throw new ArgumentOutOfRangeException(nameof(bindingTarget));
        ValidateAttachments();
        if (attachmentsDirty)
        {
            // Shared image changes can affect several FBOs; refresh each FBO lazily
            // at its next use while preserving its independently configured routing.
            RHI.RHICommandContext.Current.RefreshFramebufferAttachments(this);
            attachmentsDirty = false;
        }
        RHI.RHICommandContext.Current.SelectFramebufferOwner(this, bindingTarget);
    }
    /// <summary>Binds the default framebuffer.</summary>
    public static void Unbind() => RHI.RHICommandContext.Current.SelectFramebufferOwner(null, RHI.FramebufferSelection.ReadAndDraw);
    /// <summary>Checks completeness in all builds while preserving separate read and draw bindings.</summary>
    public bool CheckStatus(out string? errorMessage)
    {
        if (IsDisposed) { errorMessage = "Framebuffer has retired."; return false; }
        try { ValidateAttachments(); RHI.RHICommandContext.Current.RequireFramebufferComplete(this); errorMessage=null; return true; }
        catch (InvalidOperationException error) { errorMessage=error.Message; return false; }
    }
    /// <summary>Sets the debug label on the framebuffer object.</summary>
    public override void SetDebugName(string? debugName)
    {
        this.debugName = debugName;
#if DEBUG
        if (fboId != 0) GlDebug.TryLabelFramebuffer(fboId, debugName);
#endif
    }
    /// <summary>Converts a framebuffer wrapper to its current GL name for engine interop.</summary>
    public static implicit operator int(GpuFramebuffer? buffer) => buffer?.fboId ?? 0;
    #endregion
    #endregion

    #region Internal API
    /// <summary>Reports whether the manager has assigned a native framebuffer snapshot.</summary>
    internal bool HasNativeMetadata => metadata.Attachments is not null;
    /// <summary>Supplies the current immutable descriptor to command-owned composition.</summary>
    internal GpuFramebufferMetadata MetadataSnapshot => metadata;

    /// <summary>Validates supplied image snapshots without acquiring or recovering native facts.</summary>
    internal void ValidateAttachments()
    {
        ObjectDisposedException.ThrowIf(IsDisposed || fboId != 0 && !IsValid, this);
        if ((ownsFramebuffer || passMetadataPublished) && !attachmentsDirty
            && ResourceManager?.GetResourceRegistration(this) is { MetadataRevision: > 0, MetadataAvailable: false })
            throw new InvalidOperationException("Framebuffer metadata must be republished after backing resource retirement.");
        ReconcileAttachmentMetadata();
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        foreach (var pair in attachments) pair.Value.ValidateSlot(pair.Key);
    }

    /// <summary>Invalidates bindings only while this framebuffer still references the changed attachment.</summary>
    internal void OnAttachmentChanged(GpuFramebufferAttachment attachment)
    {
        if (IsDisposed || externalPublicationInProgress || !attachments.ContainsValue(attachment)) return;
        attachmentsDirty = true;
        if (!resizing) PublishAttachmentsChanged();
    }
    #endregion

    #region Protected API

    /// <summary>Identifies the underlying GL object for deletion.</summary>
    protected override GpuResourceKind ResourceKind => GpuResourceKind.Framebuffer;
    /// <summary>Prevents deletion of externally managed framebuffer names.</summary>
    protected override bool OwnsResource => ownsFramebuffer;
    /// <summary>Releases attachment references without disposing any attachment.</summary>
    protected override void OnAfterDelete()
    {
        Surface = null;
        attachments.Clear();
        PublishAttachmentsChanged();
        AttachmentsChanged = null;
    }
    /// <summary>Withdraws image references when ownership of the framebuffer name is transferred.</summary>
    protected override void OnDetached(nint id)
    {
        Surface = null;
        attachments.Clear();
        PublishAttachmentsChanged();
        AttachmentsChanged = null;
    }
    #endregion

    #region Private
    /// <summary>Notifies all dependents even when one callback fails after a committed attachment change.</summary>
    private void PublishAttachmentsChanged()
    {
        AttachmentRevision++;
        if (AttachmentsChanged is not { } callbacks) return;
        foreach (Action callback in callbacks.GetInvocationList())
        {
            try { callback(); }
            catch (Exception error) { Debug.WriteLine($"[GpuFramebuffer] Attachment observer failed: {error}"); }
        }
    }

    /// <summary>Allocates an owned framebuffer or borrows an explicit external handle.</summary>
    private protected GpuFramebuffer(bool ownsFramebuffer, int id = 0, string? name = null, int width = 0, int height = 0,
        GpuFramebufferInitialization initialization = default)
    {
        this.ownsFramebuffer = ownsFramebuffer;
        fboId = ownsFramebuffer ? 0 : id;
        wrappedWidth = width; wrappedHeight = height;
        if (ownsFramebuffer)
        {
            RHI.RHICommandContext.Current.AllocateFramebufferOwner(this, initialization);
            foreach (var pair in initialization.Attachments)
            {
                if (pair.Key == FramebufferSlot.DepthStencil)
                {
                    attachments[FramebufferSlot.Depth] = pair.Value;
                    attachments[FramebufferSlot.Stencil] = pair.Value;
                }
                else attachments[pair.Key] = pair.Value;
                pair.Value.Observe(this);
            }
            reconciledAttachmentDescriptors = metadata.Attachments;
        }
        else ResourceManager?.RegisterWrapperDeclaration(this);
        SetDebugName(name);
    }
    /// <summary>Finds the lowest color slot, then depth or stencil, for viewport compatibility.</summary>
    private GpuFramebufferAttachment? FirstAttachment
    {
        get
        {
            ReconcileAttachmentMetadata();
            int firstSlot = int.MaxValue;
            GpuFramebufferAttachment? first = null;
            foreach (var pair in attachments)
            {
                if ((int)pair.Key >= firstSlot) continue;
                firstSlot = (int)pair.Key;
                first = pair.Value;
            }
            return first;
        }
    }
    /// <summary>Validates a color index and maps it to its actual attachment point.</summary>
    private static FramebufferSlot ColorSlot(int index)
    {
        if ((uint)index > 15) throw new ArgumentOutOfRangeException(nameof(index));
        return FramebufferSlot.Color0 + index;
    }
    /// <summary>Identifies framebuffer color attachment points.</summary>
    private static bool IsColorSlot(FramebufferSlot slot) => slot >= FramebufferSlot.Color0
        && slot <= FramebufferSlot.Color15;
    #endregion
}
