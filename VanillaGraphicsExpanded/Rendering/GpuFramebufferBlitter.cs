using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns reusable scratch framebuffers for copies while borrowing source and destination attachments.</summary>
/// <remarks>The caller retains this owner across copies and disposes it on teardown. Blits preserve bindings and never change viewport or original attachment routing.</remarks>
public sealed class GpuFramebufferBlitter : IDisposable
{
    private GpuFramebuffer? blitRead;
    private GpuFramebuffer? blitDraw;
    private BorrowedColor? readColor;
    private BorrowedColor? drawColor;
    private bool disposed;

    #region Public API
    /// <summary>Copies between managed framebuffer targets without taking ownership of either target.</summary>
    public void Blit(GpuFramebuffer source, GpuFramebuffer destination,
        ClearBufferMask mask = ClearBufferMask.ColorBufferBit,
        BlitFramebufferFilter filter = BlitFramebufferFilter.Nearest)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        if (!source.IsValid || !destination.IsValid)
            throw new ArgumentException("Blit framebuffers must be valid.");
        Blit(source.FboId, source.Width, source.Height, destination.FboId, destination.Width, destination.Height, mask, filter);
    }
    
    /// <summary>Blits borrowed color attachments without changing original routing; depth/stencil copies need only bindings.</summary>
    public void Blit(int source, int sourceWidth, int sourceHeight, int destination, int destinationWidth,
        int destinationHeight, ClearBufferMask mask = ClearBufferMask.ColorBufferBit, BlitFramebufferFilter filter = BlitFramebufferFilter.Nearest)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (source < 0 || destination < 0)
            throw new ArgumentOutOfRangeException(nameof(source), "Framebuffer names cannot be negative.");
        if (sourceWidth <= 0 || sourceHeight <= 0 || destinationWidth <= 0 || destinationHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceWidth), "Blit dimensions must be positive.");
        var gl = GlStateCache.Current;
        using var bindings = gl.BindFramebufferScope();
        if ((mask & ClearBufferMask.ColorBufferBit) != 0)
        {
            // Refresh borrowed attachment names on every call so engine rebuilds and texture replacement are observed.
            int read = BorrowBlitColor(source, ref blitRead, ref readColor);
            int draw = BorrowBlitColor(destination, ref blitDraw, ref drawColor);
            gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, read);
            gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, draw);
            GL.BlitFramebuffer(0, 0, sourceWidth, sourceHeight, 0, 0, destinationWidth, destinationHeight,
                ClearBufferMask.ColorBufferBit, filter);
        }
        var remaining = mask & ~ClearBufferMask.ColorBufferBit;
        if (remaining != 0)
        {
            // Depth and stencil selection is independent of color read/draw routing.
            gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, source);
            gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, destination);
            GL.BlitFramebuffer(0, 0, sourceWidth, sourceHeight, 0, 0, destinationWidth, destinationHeight, remaining, filter);
        }
    }

    /// <summary>Releases scratch framebuffer storage and borrowed references when the caller retires its targets.</summary>
    public void Reset()
    {
        blitRead?.Dispose();
        blitDraw?.Dispose();
        blitRead = null;
        blitDraw = null;
        readColor = null;
        drawColor = null;
    }

    /// <summary>Releases only owned scratch framebuffers; borrowed textures and renderbuffers remain caller-owned.</summary>
    public void Dispose()
    {
        if (disposed) return;
        Reset();
        disposed = true;
    }
    #endregion

    #region Private
    /// <summary>Updates a borrowed image only when its framebuffer or selected attachment changes.</summary>
    private static int BorrowBlitColor(int source, ref GpuFramebuffer? scratch, ref BorrowedColor? previous)
    {
        // Default framebuffer images cannot be attached elsewhere; retain their existing buffer selection.
        if (source == 0) return 0;
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
        var attachment = new BorrowedColor(source, type, name, level, face, layer);
        if (scratch is { IsValid: true } && previous == attachment) return scratch.FboId;
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
        previous = attachment;
        return scratch.FboId;
    }
    /// <summary>Identifies the image borrowed by one scratch framebuffer, including its selected subresource.</summary>
    private readonly record struct BorrowedColor(int Framebuffer, int Type, int Name, int Level, int Face, int Layer);
    #endregion
}
