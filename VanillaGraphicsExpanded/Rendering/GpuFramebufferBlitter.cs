using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Copies between a fixed pair of borrowed framebuffers, refreshing scratch attachments only after lifecycle notifications.</summary>
public sealed class GpuFramebufferBlitter : IDisposable
{
    private readonly GpuFramebuffer source;
    private readonly GpuFramebuffer destination;
    private readonly ClearBufferMask mask;
    private GpuFramebuffer? blitRead;
    private GpuFramebuffer? blitDraw;
    private bool dirty = true;
    private bool disposed;

    #region Public API
    /// <summary>Configures reusable scratch attachments and subscribes to changes in both borrowed targets.</summary>
    public GpuFramebufferBlitter(GpuFramebuffer source, GpuFramebuffer destination,
        ClearBufferMask mask = ClearBufferMask.ColorBufferBit)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        this.source = source;
        this.destination = destination;
        this.mask = mask;
        ConfigureAttachments();
        source.AttachmentsChanged += OnAttachmentsChanged;
        destination.AttachmentsChanged += OnAttachmentsChanged;
    }

    /// <summary>Copies the configured buffers while preserving bindings; ordinary copies do not query attachment metadata.</summary>
    public void Blit(BlitFramebufferFilter filter = BlitFramebufferFilter.Nearest)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ValidateTargets();
        if (dirty) ConfigureAttachments();
        var gl = GlStateCache.Current;
        using var bindings = gl.BindFramebufferScope();
        if ((mask & ClearBufferMask.ColorBufferBit) != 0)
        {
            gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, blitRead?.FboId ?? 0);
            gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, blitDraw?.FboId ?? 0);
            GL.BlitFramebuffer(0, 0, source.Width, source.Height, 0, 0, destination.Width, destination.Height,
                ClearBufferMask.ColorBufferBit, filter);
        }
        var remaining = mask & ~ClearBufferMask.ColorBufferBit;
        if (remaining != 0)
        {
            // Depth and stencil selection is independent of color read/draw routing.
            gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, source.FboId);
            gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, destination.FboId);
            GL.BlitFramebuffer(0, 0, source.Width, source.Height, 0, 0, destination.Width, destination.Height, remaining, filter);
        }
    }

    /// <summary>Unsubscribes both targets and releases scratch framebuffers without disposing borrowed resources.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        source.AttachmentsChanged -= OnAttachmentsChanged;
        destination.AttachmentsChanged -= OnAttachmentsChanged;
        blitRead?.Dispose();
        blitDraw?.Dispose();
        blitRead = null;
        blitDraw = null;
    }
    #endregion

    #region Private
    /// <summary>Defers GL work until the next copy, allowing notifications during resource teardown.</summary>
    private void OnAttachmentsChanged()
    {
        dirty = true;
    }

    /// <summary>Rejects retired targets or absent viewport dimensions before touching driver bindings.</summary>
    private void ValidateTargets()
    {
        if (source.IsDisposed || destination.IsDisposed)
            throw new ObjectDisposedException(nameof(GpuFramebuffer), "Blit target has been retired.");
        if (source.Width <= 0 || source.Height <= 0 || destination.Width <= 0 || destination.Height <= 0)
            throw new InvalidOperationException("Blit targets require positive dimensions.");
    }

    /// <summary>Rebuilds scratch attachments once after a target notification, preserving the caller's bindings on failure.</summary>
    private void ConfigureAttachments()
    {
        ValidateTargets();
        using var bindings = GlStateCache.Current.BindFramebufferScope();
        try
        {
            if ((mask & ClearBufferMask.ColorBufferBit) != 0)
            {
                BorrowBlitColor(source.FboId, ref blitRead);
                BorrowBlitColor(destination.FboId, ref blitDraw);
            }
            dirty = false;
        }
        catch
        {
            // Constructor failures must not leave partially configured scratch resources alive.
            blitRead?.Dispose();
            blitDraw?.Dispose();
            blitRead = null;
            blitDraw = null;
            throw;
        }
    }

    /// <summary>Configures a scratch target from the current color image during initialization or notification-driven refresh.</summary>
    private static void BorrowBlitColor(int source, ref GpuFramebuffer? scratch)
    {
        // Default framebuffer images cannot be attached elsewhere; retain their existing buffer selection.
        if (source == 0) return;
        var gl = GlStateCache.Current;
        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, source);
        GL.GetFramebufferAttachmentParameter(FramebufferTarget.ReadFramebuffer, FramebufferAttachment.ColorAttachment0,
            FramebufferParameterName.FramebufferAttachmentObjectType, out int type);
        GL.GetFramebufferAttachmentParameter(FramebufferTarget.ReadFramebuffer, FramebufferAttachment.ColorAttachment0,
            FramebufferParameterName.FramebufferAttachmentObjectName, out int name);
        if (name == 0) throw new InvalidOperationException("Color blit requires attachment zero.");
        int level = 0, face = 0, layer = 0;
        if (type == (int)All.Texture)
        {
            GL.GetFramebufferAttachmentParameter(FramebufferTarget.ReadFramebuffer, FramebufferAttachment.ColorAttachment0,
                FramebufferParameterName.FramebufferAttachmentTextureLevel, out level);
            GL.GetFramebufferAttachmentParameter(FramebufferTarget.ReadFramebuffer, FramebufferAttachment.ColorAttachment0,
                FramebufferParameterName.FramebufferAttachmentTextureCubeMapFace, out face);
            GL.GetFramebufferAttachmentParameter(FramebufferTarget.ReadFramebuffer, FramebufferAttachment.ColorAttachment0,
                FramebufferParameterName.FramebufferAttachmentTextureLayer, out layer);
        }
        scratch ??= GpuFramebuffer.CreateEmpty("Framebuffer.BlitScratch");
        scratch.Bind();
        if (type == (int)All.Renderbuffer)
            GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, name);
        else if (face != 0)
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, (TextureTarget)face, name, level);
        else if (layer != 0)
            GL.FramebufferTextureLayer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, name, level, layer);
        else
            // Whole layered attachments and selected layer zero both blit from layer zero.
            GL.FramebufferTexture(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, name, level);
        GL.ReadBuffer(ReadBufferMode.ColorAttachment0);
        GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
    }
    #endregion
}
