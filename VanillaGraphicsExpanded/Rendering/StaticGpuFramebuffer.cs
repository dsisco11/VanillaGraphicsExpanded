using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns an immutable attachment layout and output route established before publication.</summary>
public sealed class StaticGpuFramebuffer : GpuFramebuffer
{
    private readonly DrawBuffersEnum[] routing;
    private readonly List<GpuResource> frozen = new();
    private readonly OpenGlStateCache configurationOwner = OpenGlStateCache.Current;
    private readonly (nint Handle, long Generation) configurationContext = Integration.RenderContextRegistry.Current();

    #region Public API
    /// <summary>Creates and freezes a complete framebuffer; backing storage remains borrowed and content writes remain permitted.</summary>
    public static StaticGpuFramebuffer Create(IReadOnlyList<GpuFramebufferAttachment> colors,
        GpuFramebufferAttachment? depth = null, string? debugName = null, int firstColorAttachment = 0)
        => new(colors, depth, debugName, firstColorAttachment);
    /// <summary>Rejects replacement of constructor-declared attachments.</summary>
    public override void SetAttachment(FramebufferAttachment slot, GpuFramebufferAttachment attachment)
        => throw new InvalidOperationException("Static framebuffer attachments cannot change.");
    /// <summary>Rejects removal of constructor-declared attachments.</summary>
    public override bool RemoveAttachment(FramebufferAttachment slot)
        => throw new InvalidOperationException("Static framebuffer attachments cannot change.");
    /// <summary>Requires replacement of the static allocation instead of resizing its attached images.</summary>
    public override bool Resize(int width, int height)
        => throw new InvalidOperationException("Recreate a static framebuffer to change its dimensions.");
    #endregion

    #region Internal API
    #region Declaration admission
    /// <summary>Certifies the immutable route once when a reusable pass declaration is constructed.</summary>
    internal override void ValidateDeclaredRouting(ReadOnlySpan<DrawBuffersEnum> route)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        for (int i = 0; i < Math.Max(route.Length, routing.Length); i++)
            if ((i < route.Length ? route[i] : DrawBuffersEnum.None) !=
                (i < routing.Length ? routing[i] : DrawBuffersEnum.None))
                throw new InvalidOperationException("A static framebuffer pass must use its constructor-declared routing.");
    }
    /// <summary>Returns an independent immutable configuration snapshot for explicit state observations.</summary>
    internal override DrawBuffersEnum[] CaptureDeclaredRouting() => (DrawBuffersEnum[])routing.Clone();
    #endregion

    #endregion

    #region Protected API
    /// <summary>Releases immutable backing guards before retiring this framebuffer, preserving borrowed storage ownership.</summary>
    protected override void OnBeforeDelete(nint id)
    {
        ReleaseConfiguration((int)id);
        base.OnBeforeDelete(id);
    }
    /// <summary>Withdraws immutable admission before transferring the native handle to another owner.</summary>
    protected override void OnDetached(nint id)
    {
        ReleaseConfiguration((int)id);
        base.OnDetached(id);
    }
    #endregion

    #region Private
    /// <summary>Builds native state transactionally, then prevents changes through every managed attachment alias.</summary>
    private StaticGpuFramebuffer(IReadOnlyList<GpuFramebufferAttachment> colors,
        GpuFramebufferAttachment? depth, string? name, int firstColorAttachment) : base(true, name: name)
    {
        try
        {
            routing = ConfigureAttachments(colors, depth, firstColorAttachment);
            foreach (var pair in AttachmentImages)
            {
                var image = pair.Value;
                if (image.Resource is not { } storage || storage is BorrowedTexture
                    || storage is GpuTextureView { Source: null or BorrowedTexture })
                    throw new ArgumentException("Static framebuffers require managed attachment storage with tracked allocation ownership.");
                Freeze(image); Freeze(storage);
            }
            configurationOwner.RegisterImmutableFramebuffer(this);
            Integration.RenderContextRegistry.Retiring += OnContextRetiring;

        }
        catch { Dispose(); throw; }
    }
    /// <summary>Guards each shared image/allocation once, without preventing writes to its existing contents.</summary>
    private void Freeze(GpuResource resource)
    {
        if (frozen.Contains(resource)) return;
        if (resource is BorrowedTexture || resource is GpuTextureView { Source: null })
            throw new ArgumentException("Static framebuffers require managed backing ownership throughout a view chain.");
        frozen.Add(resource);
        try { resource.RetainPassReference(); }
        catch { frozen.Remove(resource); throw; }
        if (resource is GpuTextureView { Source: { } source }) Freeze(source);
    }
    /// <summary>Removes static admission and releases all image/allocation guards without native mutations.</summary>
    private void ReleaseConfiguration(int id)
    {
        Integration.RenderContextRegistry.Retiring -= OnContextRetiring;
        configurationOwner.UnregisterImmutableFramebuffer(id, this);
        foreach (var resource in frozen) resource.ReleasePassReference();
        frozen.Clear();
    }
    /// <summary>Withdraws native configuration authority when its context namespace retires, without native calls.</summary>
    private void OnContextRetiring((nint Handle, long Generation) context)
    {
        if (context != configurationContext) return;
        configurationOwner.UnregisterImmutableFramebuffer(FboId, this);
        Integration.RenderContextRegistry.Retiring -= OnContextRetiring;
    }
    #endregion
}
