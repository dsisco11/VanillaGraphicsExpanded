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
    private readonly Dictionary<FramebufferAttachment, GpuFramebufferAttachment> attachments = new();
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
    /// <summary>Gets the number of occupied color slots, excluding sparse holes.</summary>
    public int ColorAttachmentCount => metadata.Attachments?.Keys.Count(IsColorSlot) ?? attachments.Keys.Count(IsColorSlot);
    /// <summary>Reports whether a depth image is configured.</summary>
    public bool HasDepthAttachment => metadata.Attachments?.ContainsKey(FramebufferAttachment.DepthAttachment) ?? attachments.ContainsKey(FramebufferAttachment.DepthAttachment);
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
    public DynamicTexture2D? DepthTexture => GetAttachment(FramebufferAttachment.DepthAttachment)?.Resource as DynamicTexture2D;
    /// <summary>Gets a managed depth renderbuffer when present.</summary>
    public GpuRenderbuffer? DepthRenderbuffer => GetAttachment(FramebufferAttachment.DepthAttachment)?.Resource as GpuRenderbuffer;
    #endregion

    #region Attachments
    /// <summary>Gets an exact slot; packed depth/stencil requires both aspects to share the same image.</summary>
    public GpuFramebufferAttachment? GetAttachment(FramebufferAttachment slot)
    {
        ReconcileAttachmentMetadata();
        if (slot == FramebufferAttachment.DepthStencilAttachment)
        {
            var depth = GetAttachment(FramebufferAttachment.DepthAttachment);
            return ReferenceEquals(depth, GetAttachment(FramebufferAttachment.StencilAttachment)) ? depth : null;
        }
        return attachments.GetValueOrDefault(slot);
    }

    /// <summary>Configures an image while preserving bindings and retaining a non-owning strong reference.</summary>
    public abstract void SetAttachment(FramebufferAttachment slot, GpuFramebufferAttachment attachment);

    /// <summary>Detaches images and releases references without disposing shared attachment instances.</summary>
    public abstract bool RemoveAttachment(FramebufferAttachment slot);

    /// <summary>Gets a live color texture name, or zero for absent and renderbuffer-backed slots.</summary>
    public int GetColorTextureId(int index) => IsDisposed ? 0 : GetAttachment(ColorSlot(index))?.TextureId ?? 0;
    /// <summary>Gets the live depth texture name when available.</summary>
    public int GetDepthTextureId() => IsDisposed ? 0 : GetAttachment(FramebufferAttachment.DepthAttachment)?.TextureId ?? 0;
    /// <summary>Gets the live depth renderbuffer name when available.</summary>
    public int GetDepthRenderbufferId() => IsDisposed ? 0 : GetAttachment(FramebufferAttachment.DepthAttachment)?.RenderbufferId ?? 0;

    /// <summary>Resizes distinct whole backing resources and publishes one completed update for this framebuffer.</summary>
    public abstract bool Resize(int newWidth, int newHeight);

    #endregion

    #region Rendering
    /// <summary>Binds this framebuffer after rejecting retired images and refreshing changed image bindings.</summary>
    public void Bind() => Bind(FramebufferTarget.Framebuffer);

    /// <summary>Refreshes changed images on the draw binding, optionally selecting the read alias for compatibility.</summary>
    internal void Bind(FramebufferTarget bindingTarget)
    {
        if (bindingTarget is not (FramebufferTarget.Framebuffer or FramebufferTarget.DrawFramebuffer))
            throw new ArgumentOutOfRangeException(nameof(bindingTarget));
        ValidateAttachments();
        StateCache.Current.BindFramebufferResource(this, bindingTarget);
        if (attachmentsDirty)
        {
            StateCache.Current.RequireMutableFramebufferConfiguration(fboId);
            // Shared image changes can affect several FBOs; refresh each FBO lazily
            // at its next use while preserving its independently configured routing.
            StateCache.Current.ReattachFramebuffer(this, attachments);
            attachmentsDirty = false;
        }
    }
    /// <summary>Binds the default framebuffer.</summary>
    public static void Unbind() => StateCache.Current.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    /// <summary>Binds this framebuffer and its selected image dimensions as the viewport.</summary>
    public void BindWithViewport()
    {
        Bind();
        StateCache.Current.ApplyDynamic(new Pipeline.State.DynamicDrawState { Width = Width, Height = Height });
    }
    /// <summary>Clears the currently bound framebuffer with the requested aspects.</summary>
    public void Clear(ClearBufferMask mask = ClearBufferMask.ColorBufferBit)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        GL.Clear(mask);
    }
    /// <summary>Sets the clear color and clears the currently bound framebuffer.</summary>
    public void Clear(float r, float g, float b, float a, ClearBufferMask mask = ClearBufferMask.ColorBufferBit)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        StateCache.Current.SetClearColor(r, g, b, a);
        GL.Clear(mask);
    }
    /// <summary>Binds and clears this framebuffer, leaving it bound for subsequent drawing.</summary>
    public void BindAndClear(float r = 0, float g = 0, float b = 0, float a = 0,
        ClearBufferMask mask = ClearBufferMask.ColorBufferBit)
    {
        Bind();
        Clear(r, g, b, a, mask);
    }
    /// <summary>Checks completeness in all builds while preserving separate read and draw bindings.</summary>
    public bool CheckStatus(out string? errorMessage)
    {
        if (IsDisposed) { errorMessage = "Framebuffer has retired."; return false; }
        using var bindings = StateCache.Current.BindFramebufferScope();
        Bind();
        var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        errorMessage = status == FramebufferErrorCode.FramebufferComplete ? null : $"Framebuffer incomplete: {status}";
        return errorMessage is null;
    }
    /// <summary>Saves the current draw framebuffer binding for legacy callers.</summary>
    public static int SaveBinding() => StateCache.Current.GetCurrentFramebuffer(FramebufferTarget.Framebuffer);
    /// <summary>Restores both framebuffer targets for callers with one saved binding.</summary>
    public static void RestoreBinding(int fboId) => StateCache.Current.BindFramebuffer(FramebufferTarget.Framebuffer, fboId);
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

    /// <summary>Validates registered image snapshots and refreshes context-local selections with binding-preserving native reads.</summary>
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
        if (IsDisposed || !attachments.ContainsValue(attachment)) return;
        attachmentsDirty = true;
        if (!resizing) PublishAttachmentsChanged();
    }
    #endregion

    #region Protected API

    /// <summary>Identifies the underlying GL object for deletion.</summary>
    protected override GpuResourceKind ResourceKind => GpuResourceKind.Framebuffer;
    /// <summary>Prevents deletion of externally managed framebuffer names.</summary>
    protected override bool OwnsResource => ownsFramebuffer;
    /// <summary>Withdraws routing knowledge when an owned native identity is logically retired.</summary>
    protected override void OnBeforeDelete(nint id)
    {
        if (ownsFramebuffer) StateCache.Current.InvalidateFramebufferDrawRouting((int)id);
    }
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
        StateCache.Current.InvalidateFramebufferDrawRouting((int)id);
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
    private protected GpuFramebuffer(bool ownsFramebuffer, int id = 0, string? name = null, int width = 0, int height = 0)
    {
        this.ownsFramebuffer = ownsFramebuffer;
        fboId = ownsFramebuffer ? 0 : id;
        wrappedWidth = width; wrappedHeight = height;
        if (ownsFramebuffer)
        {
            if (!GpuResourceManager.CreateFramebuffer(new GpuFramebufferInitialization(), this, out _).HasValue)
                throw new InvalidOperationException("Framebuffer allocation failed.");
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
    private static FramebufferAttachment ColorSlot(int index)
    {
        if ((uint)index > 15) throw new ArgumentOutOfRangeException(nameof(index));
        return FramebufferAttachment.ColorAttachment0 + index;
    }
    /// <summary>Identifies framebuffer color attachment points.</summary>
    private static bool IsColorSlot(FramebufferAttachment slot) => slot >= FramebufferAttachment.ColorAttachment0
        && slot <= FramebufferAttachment.ColorAttachment15;
    #endregion
}
