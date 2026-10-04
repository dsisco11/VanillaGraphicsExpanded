using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Creates framebuffer configurations while leaving all image ownership with attachment instances.</summary>
public sealed partial class GpuFramebuffer
{
    #region Public API
    /// <summary>Creates an MRT borrowing attachment instances and optional depth/stencil storage.</summary>
    /// <param name="colors">Borrowed color images in consecutive attachment order; an empty list disables color reads and writes.</param>
    /// <param name="depth">Optional borrowed depth, stencil, or packed depth-stencil image.</param>
    /// <param name="debugName">Optional diagnostic label for the framebuffer.</param>
    /// <param name="firstColorAttachment">First color output slot; preceding slots have no image and discard shader output.</param>
    /// <returns>A complete framebuffer that owns its FBO handle but does not own the supplied attachments.</returns>
    public static GpuFramebuffer Create(IReadOnlyList<GpuFramebufferAttachment> colors,
        GpuFramebufferAttachment? depth = null, string? debugName = null, int firstColorAttachment = 0)
    {
        ArgumentNullException.ThrowIfNull(colors);
        if (firstColorAttachment < 0 || firstColorAttachment > 31 || colors.Count > 32 - firstColorAttachment)
            throw new ArgumentOutOfRangeException(nameof(firstColorAttachment));
        using var bindings = StateCache.Current.BindFramebufferScope();
        var framebuffer = CreateEmpty(debugName);
        try
        {
            // Borrow image instances first, then establish routing for this configuration.
            for (int i = 0; i < colors.Count; i++) framebuffer.SetAttachment(ColorSlot(firstColorAttachment + i), colors[i]);
            if (depth is not null) framebuffer.SetAttachment(depth.DepthStencilSlot, depth);
            framebuffer.Bind();
            // Preserve shader locations without allocating dummy images for leading holes.
            var drawBuffers = colors.Count == 0 ? Array.Empty<DrawBuffersEnum>()
                : Enumerable.Range(0, firstColorAttachment + colors.Count)
                    .Select(i => i < firstColorAttachment ? DrawBuffersEnum.None : DrawBuffersEnum.ColorAttachment0 + i).ToArray();
            if (drawBuffers.Length != 0)
            {
                GL.DrawBuffers(drawBuffers.Length, drawBuffers);
                GL.ReadBuffer(ReadBufferMode.ColorAttachment0 + firstColorAttachment);
            }
            else
            {
                GL.DrawBuffer(DrawBufferMode.None);
                GL.ReadBuffer(ReadBufferMode.None);
            }
            if (!framebuffer.CheckStatus(out var error)) throw new InvalidOperationException(error);
            return framebuffer;
        }
        // Failure retires only the FBO; the caller still owns every supplied image.
        catch { framebuffer.Dispose(); throw; }
    }

    /// <summary>Creates a framebuffer borrowing existing color and optional depth textures.</summary>
    /// <param name="colorTexture">The existing color texture to borrow at attachment zero.</param>
    /// <param name="depthTexture">Optional existing depth or packed depth-stencil texture to borrow.</param>
    /// <param name="debugName">Optional diagnostic label for the framebuffer.</param>
    /// <returns>A new framebuffer, or null if the color texture is null or invalid.</returns>
    public static GpuFramebuffer? CreateSingle(DynamicTexture2D? colorTexture,
        DynamicTexture2D? depthTexture = null, string? debugName = null)
    {
        if (colorTexture is not { IsValid: true }) return null;
        return Create([GpuFramebufferAttachment.FromTexture(colorTexture)],
            depthTexture is null ? null : GpuFramebufferAttachment.FromTexture(depthTexture), debugName);
    }

    /// <summary>Creates a framebuffer borrowing an existing color texture and depth renderbuffer.</summary>
    /// <param name="colorTexture">The existing color texture to borrow at attachment zero.</param>
    /// <param name="depthRenderbuffer">Optional existing depth or packed depth-stencil renderbuffer to borrow.</param>
    /// <param name="debugName">Optional diagnostic label for the framebuffer.</param>
    /// <returns>A new framebuffer, or null if the color texture is null or invalid.</returns>
    public static GpuFramebuffer? CreateSingle(DynamicTexture2D? colorTexture,
        GpuRenderbuffer? depthRenderbuffer, string? debugName = null)
    {
        if (colorTexture is not { IsValid: true }) return null;
        return Create([GpuFramebufferAttachment.FromTexture(colorTexture)],
            depthRenderbuffer is null ? null : GpuFramebufferAttachment.FromRenderbuffer(depthRenderbuffer), debugName);
    }

    /// <summary>Creates an MRT borrowing textures without filtering or renumbering invalid inputs.</summary>
    /// <param name="colorTextures">Existing color textures to borrow in consecutive attachment order; every element must be valid.</param>
    /// <param name="depthTexture">Optional existing depth or packed depth-stencil texture to borrow.</param>
    /// <param name="debugName">Optional diagnostic label for the framebuffer.</param>
    /// <returns>A new MRT framebuffer, or null if the color texture array is null or empty.</returns>
    public static GpuFramebuffer? CreateMRT(DynamicTexture2D[]? colorTextures,
        DynamicTexture2D? depthTexture = null, string? debugName = null)
    {
        if (colorTextures is null || colorTextures.Length == 0) return null;
        return Create(colorTextures.Select(texture => GpuFramebufferAttachment.FromTexture(texture)).ToArray(),
            depthTexture is null ? null : GpuFramebufferAttachment.FromTexture(depthTexture), debugName);
    }

    /// <summary>Creates an MRT borrowing existing textures and a depth renderbuffer.</summary>
    /// <param name="colorTextures">Existing color textures to borrow in consecutive attachment order; every element must be valid.</param>
    /// <param name="depthRenderbuffer">Optional existing depth or packed depth-stencil renderbuffer to borrow.</param>
    /// <param name="debugName">Optional diagnostic label for the framebuffer.</param>
    /// <returns>A new MRT framebuffer, or null if the color texture array is null or empty.</returns>
    public static GpuFramebuffer? CreateMRT(DynamicTexture2D[]? colorTextures,
        GpuRenderbuffer? depthRenderbuffer, string? debugName = null)
    {
        if (colorTextures is null || colorTextures.Length == 0) return null;
        return Create(colorTextures.Select(texture => GpuFramebufferAttachment.FromTexture(texture)).ToArray(),
            depthRenderbuffer is null ? null : GpuFramebufferAttachment.FromRenderbuffer(depthRenderbuffer), debugName);
    }

    /// <summary>Creates a color-only MRT from a texture argument list.</summary>
    /// <param name="colorTextures">Existing color textures to borrow in consecutive attachment order; every element must be valid.</param>
    /// <returns>A new color-only MRT framebuffer, or null if the texture array is null or empty.</returns>
    public static GpuFramebuffer? CreateMRT(params DynamicTexture2D[] colorTextures)
        => CreateMRT(colorTextures, depthTexture: null);

    /// <summary>Creates a named color-only MRT from a texture argument list.</summary>
    /// <param name="debugName">Optional diagnostic label for the framebuffer.</param>
    /// <param name="colorTextures">Existing color textures to borrow in consecutive attachment order; every element must be valid.</param>
    /// <returns>A new color-only MRT framebuffer, or null if the texture array is null or empty.</returns>
    public static GpuFramebuffer? CreateMRT(string? debugName, params DynamicTexture2D[] colorTextures)
        => CreateMRT(colorTextures, depthTexture: null, debugName: debugName);

    /// <summary>Creates a framebuffer borrowing depth-capable texture storage.</summary>
    /// <param name="depthTexture">The existing depth or packed depth-stencil texture to borrow.</param>
    /// <param name="debugName">Optional diagnostic label for the framebuffer.</param>
    /// <returns>A framebuffer with color reads and writes disabled, or null if the depth texture is null or invalid.</returns>
    public static GpuFramebuffer? CreateDepthOnly(DynamicTexture2D? depthTexture, string? debugName = null)
    {
        if (depthTexture is not { IsValid: true }) return null;
        if (!TextureFormatHelper.IsDepthFormat(depthTexture.InternalFormat))
            throw new ArgumentException("Depth storage is required.", nameof(depthTexture));
        return Create([], GpuFramebufferAttachment.FromTexture(depthTexture), debugName);
    }

    /// <summary>Creates a framebuffer borrowing depth-capable renderbuffer storage.</summary>
    /// <param name="depthRenderbuffer">The existing depth or packed depth-stencil renderbuffer to borrow.</param>
    /// <param name="debugName">Optional diagnostic label for the framebuffer.</param>
    /// <returns>A framebuffer with color reads and writes disabled, or null if the renderbuffer is null or invalid.</returns>
    public static GpuFramebuffer? CreateDepthOnly(GpuRenderbuffer? depthRenderbuffer, string? debugName = null)
    {
        if (depthRenderbuffer is not { IsValid: true }) return null;
        return Create([], GpuFramebufferAttachment.FromRenderbuffer(depthRenderbuffer), debugName);
    }

    /// <summary>Wraps an external framebuffer, including the default framebuffer, without acquiring ownership.</summary>
    /// <param name="existingFboId">The externally owned framebuffer name, or zero for the default framebuffer.</param>
    /// <param name="debugName">Optional diagnostic name for the wrapper.</param>
    /// <param name="width">Optional viewport width; supply two positive dimensions or leave both dimensions zero.</param>
    /// <param name="height">Optional viewport height; this metadata does not transfer attachment ownership.</param>
    /// <returns>A wrapper that owns neither the external framebuffer nor its attachments.</returns>
    public static GpuFramebuffer Wrap(int existingFboId, string? debugName = null, int width = 0, int height = 0)
    {
        if (existingFboId < 0 || width < 0 || height < 0 || (width == 0) != (height == 0))
            throw new ArgumentOutOfRangeException(nameof(existingFboId));
        return new(false) { fboId = existingFboId, debugName = debugName, wrappedWidth = width, wrappedHeight = height };
    }

    /// <summary>Refreshes external storage identity even when dimensions and integer GL names are unchanged.</summary>
    /// <param name="existingFboId">The positive framebuffer name published by the external owner after rebuilding.</param>
    /// <param name="width">The positive viewport width of the rebuilt framebuffer.</param>
    /// <param name="height">The positive viewport height of the rebuilt framebuffer.</param>
    /// <remarks>Clears cached attachment references and always notifies subscribers, including for equal-size rebuilds.</remarks>
    public void RefreshWrappedFramebuffer(int existingFboId, int width, int height)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (ownsFramebuffer) throw new InvalidOperationException("Only borrowed framebuffers can be refreshed.");
        if (existingFboId <= 0 || width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(existingFboId));
        attachments.Clear();
        attachmentsDirty = false;
        fboId = existingFboId;
        wrappedWidth = width;
        wrappedHeight = height;
        PublishAttachmentsChanged();
    }

    /// <summary>Allocates an empty framebuffer without allocating or owning attachment instances.</summary>
    /// <param name="debugName">Optional diagnostic label for the framebuffer.</param>
    /// <returns>A framebuffer that owns its FBO handle and initially has no attachments.</returns>
    /// <remarks>Configure images with SetAttachment or the scratch Attach overloads before rendering.</remarks>
    public static GpuFramebuffer CreateEmpty(string? debugName = null)
    {
        var framebuffer = new GpuFramebuffer(true) { fboId = GL.GenFramebuffer() };
        if (framebuffer.fboId == 0) throw new InvalidOperationException("Framebuffer allocation failed.");
        framebuffer.SetDebugName(debugName);
        return framebuffer;
    }
    #endregion
}
