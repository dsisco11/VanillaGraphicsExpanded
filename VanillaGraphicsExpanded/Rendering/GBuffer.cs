using System;
using System.Collections.Generic;
using System.Diagnostics;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Encapsulates an OpenGL framebuffer object (FBO) with lifecycle management.
/// Supports single color attachment, multiple render targets (MRT), and depth-only configurations.
/// </summary>
/// <remarks>
/// Usage:
/// <code>
/// // Single attachment
/// using var colorTex = DynamicTexture.Create(1920, 1080, PixelInternalFormat.Rgba16f);
/// using var fbo = GBuffer.CreateSingle(colorTex);
/// 
/// // Multiple render targets
/// using var tex0 = DynamicTexture.Create(1920, 1080, PixelInternalFormat.Rgba16f);
/// using var tex1 = DynamicTexture.Create(1920, 1080, PixelInternalFormat.Rgba16f);
/// using var mrtFbo = GBuffer.CreateMRT(tex0, tex1);
/// 
/// fbo.Bind();
/// // ... render ...
/// fbo.Unbind();
/// </code>
/// </remarks>
public sealed class GpuFramebuffer : GpuResource, IDisposable
{
    #region Fields

    private int fboId;
    private readonly List<DynamicTexture2D> colorAttachments;
    private DynamicTexture2D? depthAttachment;
    private readonly bool ownsFramebuffer;
    private readonly bool ownsTextures;
    private string? debugName;

    #endregion

    #region Properties

    /// <summary>
    /// OpenGL framebuffer ID. Returns 0 if disposed or not created.
    /// </summary>
    public int FboId => fboId;

    protected override int ResourceId
    {
        get => fboId;
        set => fboId = value;
    }

    protected override GpuResourceKind ResourceKind => GpuResourceKind.Framebuffer;

    protected override bool OwnsResource => ownsFramebuffer;

    /// <summary>
    /// Number of color attachments.
    /// </summary>
    public int ColorAttachmentCount => colorAttachments.Count;

    /// <summary>
    /// Whether this FBO has a depth attachment.
    /// </summary>
    public bool HasDepthAttachment => depthAttachment != null;

    public string? DebugName => debugName;

    /// <summary>
    /// Width of the framebuffer (from first color attachment or depth).
    /// </summary>
    public int Width => colorAttachments.Count > 0 
        ? colorAttachments[0].Width 
        : depthAttachment?.Width ?? 0;

    /// <summary>
    /// Height of the framebuffer (from first color attachment or depth).
    /// </summary>
    public int Height => colorAttachments.Count > 0 
        ? colorAttachments[0].Height 
        : depthAttachment?.Height ?? 0;

    #endregion

    #region Indexer

    /// <summary>
    /// Gets a color attachment by index.
    /// </summary>
    public DynamicTexture2D this[int index] => colorAttachments[index];

    /// <summary>
    /// Gets the depth attachment texture (may be null).
    /// </summary>
    public DynamicTexture2D? DepthTexture => depthAttachment;

    #endregion

    #region Constructor (private - use factory methods)

    private GpuFramebuffer(bool ownsFramebuffer, bool ownsTextures)
    {
        colorAttachments = [];
        this.ownsFramebuffer = ownsFramebuffer;
        this.ownsTextures = ownsTextures;
    }

    #endregion

    #region Factory Methods

    /// <summary>
    /// Creates a framebuffer with a single color attachment.
    /// </summary>
    /// <param name="colorTexture">The color texture to attach.</param>
    /// <param name="depthTexture">Optional depth texture to attach.</param>
    /// <param name="ownsTextures">If true, textures will be disposed when the GBuffer is disposed.</param>
    /// <returns>A new GBuffer instance.</returns>
    public static GpuFramebuffer? CreateSingle(
        DynamicTexture2D? colorTexture,
        DynamicTexture2D? depthTexture = null,
        bool ownsTextures = false,
        string? debugName = null)
    {
        if (colorTexture == null || !colorTexture.IsValid)
        {
            Debug.WriteLine("[GBuffer] CreateSingle called with null or invalid color texture");
            return null;
        }

        var buffer = new GpuFramebuffer(ownsFramebuffer: true, ownsTextures: ownsTextures);
        buffer.colorAttachments.Add(colorTexture);
        buffer.depthAttachment = depthTexture;
        buffer.debugName = debugName;
        buffer.CreateFramebuffer();

        return buffer;
    }

    /// <summary>
    /// Creates a framebuffer with multiple render targets (MRT).
    /// </summary>
    /// <param name="colorTextures">Array of color textures to attach.</param>
    /// <param name="depthTexture">Optional depth texture to attach.</param>
    /// <param name="ownsTextures">If true, textures will be disposed when the GBuffer is disposed.</param>
    /// <returns>A new GBuffer instance.</returns>
    public static GpuFramebuffer? CreateMRT(
        DynamicTexture2D[]? colorTextures,
        DynamicTexture2D? depthTexture = null,
        bool ownsTextures = false,
        string? debugName = null)
    {
        if (colorTextures == null || colorTextures.Length == 0)
        {
            Debug.WriteLine("[GBuffer] CreateMRT called with null or empty texture array");
            return null;
        }

        // Filter out invalid textures
        var validTextures = new List<DynamicTexture2D>();
        foreach (var tex in colorTextures)
        {
            if (tex != null && tex.IsValid)
                validTextures.Add(tex);
            else
                Debug.WriteLine("[GBuffer] CreateMRT: skipping null or invalid texture");
        }

        if (validTextures.Count == 0)
        {
            Debug.WriteLine("[GBuffer] CreateMRT: no valid textures provided");
            return null;
        }

        var buffer = new GpuFramebuffer(ownsFramebuffer: true, ownsTextures: ownsTextures);
        buffer.colorAttachments.AddRange(validTextures);
        buffer.depthAttachment = depthTexture;
        buffer.debugName = debugName;
        buffer.CreateFramebuffer();

        return buffer;
    }

    /// <summary>
    /// Creates a framebuffer with multiple render targets (MRT) using params.
    /// </summary>
    /// <param name="colorTextures">Color textures to attach.</param>
    /// <returns>A new GBuffer instance.</returns>
    public static GpuFramebuffer? CreateMRT(params DynamicTexture2D[] colorTextures)
    {
        return CreateMRT(colorTextures, null, false);
    }

    public static GpuFramebuffer? CreateMRT(string? debugName, params DynamicTexture2D[] colorTextures)
    {
        return CreateMRT(colorTextures, null, false, debugName);
    }

    /// <summary>
    /// Creates a depth-only framebuffer (no color attachments).
    /// </summary>
    /// <param name="depthTexture">The depth texture to attach.</param>
    /// <param name="ownsTextures">If true, texture will be disposed when the GBuffer is disposed.</param>
    /// <returns>A new GBuffer instance.</returns>
    public static GpuFramebuffer? CreateDepthOnly(
        DynamicTexture2D? depthTexture,
        bool ownsTextures = false,
        string? debugName = null)
    {
        if (depthTexture == null || !depthTexture.IsValid)
        {
            Debug.WriteLine("[GBuffer] CreateDepthOnly called with null or invalid depth texture");
            return null;
        }

        if (!TextureFormatHelper.IsDepthFormat(depthTexture.InternalFormat))
        {
            Debug.WriteLine($"[GBuffer] CreateDepthOnly: texture format {depthTexture.InternalFormat} is not a depth format");
            return null;
        }

        var buffer = new GpuFramebuffer(ownsFramebuffer: true, ownsTextures: ownsTextures);
        buffer.depthAttachment = depthTexture;
        buffer.debugName = debugName;
        buffer.CreateFramebuffer();

        return buffer;
    }

    /// <summary>
    /// Creates a GBuffer by wrapping an existing OpenGL FBO ID.
    /// Useful for working with VS-managed framebuffers.
    /// </summary>
    /// <param name="existingFboId">The existing FBO ID to wrap.</param>
    /// <returns>A GBuffer wrapper (does not own the FBO).</returns>
    public static GpuFramebuffer Wrap(int existingFboId, string? debugName = null)
    {
        var buffer = new GpuFramebuffer(ownsFramebuffer: false, ownsTextures: false)
        {
            fboId = existingFboId,
            debugName = debugName
        };
        return buffer;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Binds this framebuffer for rendering.
    /// </summary>
    public void Bind()
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GBuffer] Attempted to bind disposed or invalid framebuffer");
            return;
        }
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fboId);
    }

    /// <summary>
    /// Unbinds any framebuffer, returning to the default framebuffer.
    /// </summary>
    public static void Unbind()
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    /// <summary>
    /// Binds this framebuffer and sets the viewport to match its dimensions.
    /// </summary>
    public void BindWithViewport()
    {
        Bind();
        GL.Viewport(0, 0, Width, Height);
    }

    /// <summary>
    /// Attaches a color texture to this framebuffer at the given color attachment index.
    /// Useful for reusing a single FBO as a scratch target.
    /// </summary>
    /// <remarks>
    /// This updates the framebuffer attachment in OpenGL. It does not take ownership of the texture.
    /// </remarks>
    public void AttachColor(DynamicTexture2D texture, int attachmentIndex = 0, int mipLevel = 0)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GBuffer] Attempted to attach color texture to disposed or invalid framebuffer");
            return;
        }

        if (texture is null || !texture.IsValid)
        {
            Debug.WriteLine("[GBuffer] Attempted to attach null/invalid color texture");
            return;
        }

        if (attachmentIndex < 0 || attachmentIndex > 15)
        {
            Debug.WriteLine("[GBuffer] Invalid color attachment index (0..15)");
            return;
        }

        Bind();
        var attachment = FramebufferAttachment.ColorAttachment0 + attachmentIndex;
        GL.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            attachment,
            TextureTarget.Texture2D,
            texture.TextureId,
            mipLevel);

        // Ensure the active draw buffer matches the attachment we write to.
        GL.DrawBuffer(DrawBufferMode.ColorAttachment0 + attachmentIndex);

        // Keep a best-effort record of attachments for convenience (does not imply ownership).
        while (colorAttachments.Count <= attachmentIndex)
        {
            // Placeholder; will be replaced by the actual attachment for this index.
            colorAttachments.Add(texture);
        }

        colorAttachments[attachmentIndex] = texture;
    }

    /// <summary>
    /// Attaches an existing OpenGL texture id as a color attachment.
    /// Intended for interop with engine-owned textures.
    /// </summary>
    public void AttachColorTextureId(int textureId, int attachmentIndex = 0, int mipLevel = 0)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GBuffer] Attempted to attach color texture id to disposed or invalid framebuffer");
            return;
        }

        if (textureId == 0)
        {
            Debug.WriteLine("[GBuffer] Attempted to attach invalid color texture id");
            return;
        }

        if (attachmentIndex < 0 || attachmentIndex > 15)
        {
            Debug.WriteLine("[GBuffer] Invalid color attachment index (0..15)");
            return;
        }

        Bind();
        var attachment = FramebufferAttachment.ColorAttachment0 + attachmentIndex;
        GL.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            attachment,
            TextureTarget.Texture2D,
            textureId,
            mipLevel);
        GL.DrawBuffer(DrawBufferMode.ColorAttachment0 + attachmentIndex);
    }

    /// <summary>
    /// Clears the framebuffer with the specified mask.
    /// The framebuffer must be bound first.
    /// </summary>
    /// <param name="mask">Clear buffer mask (Color, Depth, Stencil).</param>
    public void Clear(ClearBufferMask mask = ClearBufferMask.ColorBufferBit)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GBuffer] Attempted to clear disposed or invalid framebuffer");
            return;
        }
        GL.Clear(mask);
    }

    /// <summary>
    /// Sets the clear color and clears the framebuffer.
    /// </summary>
    public void Clear(float r, float g, float b, float a, ClearBufferMask mask = ClearBufferMask.ColorBufferBit)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GBuffer] Attempted to clear disposed or invalid framebuffer");
            return;
        }
        GL.ClearColor(r, g, b, a);
        GL.Clear(mask);
    }

    /// <summary>
    /// Checks the framebuffer status and returns whether it's complete.
    /// In release builds, always returns true without checking.
    /// </summary>
    /// <param name="errorMessage">Error message if incomplete.</param>
    /// <returns>True if complete (or release build), false if incomplete.</returns>
    public bool CheckStatus(out string? errorMessage)
    {
#if DEBUG
        if (!IsValid)
        {
            errorMessage = "Framebuffer is disposed or invalid";
            Debug.WriteLine($"[GBuffer] {errorMessage}");
            return false;
        }
        
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fboId);
        var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

        if (status != FramebufferErrorCode.FramebufferComplete)
        {
            errorMessage = $"Framebuffer incomplete: {status}";
            return false;
        }
#endif
        errorMessage = null;
        return true;
    }

    /// <summary>
    /// Gets the texture ID of a color attachment.
    /// </summary>
    /// <param name="index">Attachment index (0-based).</param>
    /// <returns>OpenGL texture ID.</returns>
    public int GetColorTextureId(int index)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GBuffer] Attempted to get texture from disposed or invalid framebuffer");
            return 0;
        }
        if (index < 0 || index >= colorAttachments.Count)
        {
            Debug.WriteLine($"[GBuffer] Color attachment index {index} out of range (0-{colorAttachments.Count - 1})");
            return 0;
        }
        return colorAttachments[index].TextureId;
    }

    /// <summary>
    /// Gets the texture ID of the depth attachment.
    /// </summary>
    /// <returns>OpenGL texture ID, or 0 if no depth attachment.</returns>
    public int GetDepthTextureId()
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GBuffer] Attempted to get depth texture from disposed or invalid framebuffer");
            return 0;
        }
        return depthAttachment?.TextureId ?? 0;
    }

    /// <summary>
    /// Resizes all attached textures.
    /// </summary>
    /// <param name="newWidth">New width in pixels.</param>
    /// <param name="newHeight">New height in pixels.</param>
    /// <returns>True if resize occurred.</returns>
    public bool Resize(int newWidth, int newHeight)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GBuffer] Attempted to resize disposed or invalid framebuffer");
            return false;
        }

        bool resized = false;

        foreach (var texture in colorAttachments)
        {
            resized |= texture.Resize(newWidth, newHeight);
        }

        if (depthAttachment != null)
        {
            resized |= depthAttachment.Resize(newWidth, newHeight);
        }

        return resized;
    }

    /// <summary>
    /// Binds the framebuffer and clears it with the specified color and mask.
    /// Restores the previous framebuffer binding after clearing.
    /// </summary>
    /// <param name="r">Red component (0-1).</param>
    /// <param name="g">Green component (0-1).</param>
    /// <param name="b">Blue component (0-1).</param>
    /// <param name="a">Alpha component (0-1).</param>
    /// <param name="mask">Clear buffer mask.</param>
    public void BindAndClear(float r = 0f, float g = 0f, float b = 0f, float a = 0f, 
        ClearBufferMask mask = ClearBufferMask.ColorBufferBit)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GBuffer] Attempted to bind and clear disposed or invalid framebuffer");
            return;
        }

        Bind();
        GL.ClearColor(r, g, b, a);
        GL.Clear(mask);
    }

    /// <summary>
    /// Blits (copies) from another GBuffer to this one.
    /// Handles binding both framebuffers and restoring state.
    /// </summary>
    /// <param name="source">Source GBuffer to copy from.</param>
    /// <param name="mask">Buffer mask to copy (Color, Depth, or Stencil).</param>
    /// <param name="filter">Blit filter (Nearest or Linear).</param>
    public void BlitFrom(GpuFramebuffer source,
        ClearBufferMask mask = ClearBufferMask.ColorBufferBit,
        BlitFramebufferFilter filter = BlitFramebufferFilter.Nearest)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GBuffer] Attempted to blit to disposed or invalid framebuffer");
            return;
        }

        if (source == null || !source.IsValid)
        {
            Debug.WriteLine("[GBuffer] Attempted to blit from null or invalid source framebuffer");
            return;
        }

        BlitFromInternal(source.FboId, source.Width, source.Height, mask, filter);
    }

    /// <summary>
    /// Blits (copies) from an external framebuffer ID to this one.
    /// Use this overload for VS-managed framebuffers.
    /// </summary>
    /// <param name="sourceFboId">Source framebuffer ID to copy from.</param>
    /// <param name="srcWidth">Source width.</param>
    /// <param name="srcHeight">Source height.</param>
    /// <param name="mask">Buffer mask to copy (Color, Depth, or Stencil).</param>
    /// <param name="filter">Blit filter (Nearest or Linear).</param>
    public void BlitFromExternal(int sourceFboId, int srcWidth, int srcHeight,
        ClearBufferMask mask = ClearBufferMask.ColorBufferBit,
        BlitFramebufferFilter filter = BlitFramebufferFilter.Nearest)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GBuffer] Attempted to blit to disposed or invalid framebuffer");
            return;
        }

        BlitFromInternal(sourceFboId, srcWidth, srcHeight, mask, filter);
    }

    /// <summary>
    /// Blits (copies) from this framebuffer to another GBuffer.
    /// </summary>
    /// <param name="dest">Destination GBuffer.</param>
    /// <param name="mask">Buffer mask to copy.</param>
    /// <param name="filter">Blit filter.</param>
    public void BlitTo(GpuFramebuffer dest,
        ClearBufferMask mask = ClearBufferMask.ColorBufferBit,
        BlitFramebufferFilter filter = BlitFramebufferFilter.Nearest)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GBuffer] Attempted to blit from disposed or invalid framebuffer");
            return;
        }

        if (dest == null || !dest.IsValid)
        {
            Debug.WriteLine("[GBuffer] Attempted to blit to null or invalid destination framebuffer");
            return;
        }

        BlitToInternal(dest.FboId, dest.Width, dest.Height, mask, filter);
    }

    /// <summary>
    /// Blits (copies) from this framebuffer to an external framebuffer ID.
    /// Use this overload for VS-managed framebuffers.
    /// </summary>
    /// <param name="destFboId">Destination framebuffer ID.</param>
    /// <param name="dstWidth">Destination width.</param>
    /// <param name="dstHeight">Destination height.</param>
    /// <param name="mask">Buffer mask to copy.</param>
    /// <param name="filter">Blit filter.</param>
    public void BlitToExternal(int destFboId, int dstWidth, int dstHeight,
        ClearBufferMask mask = ClearBufferMask.ColorBufferBit,
        BlitFramebufferFilter filter = BlitFramebufferFilter.Nearest)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GBuffer] Attempted to blit from disposed or invalid framebuffer");
            return;
        }

        BlitToInternal(destFboId, dstWidth, dstHeight, mask, filter);
    }

    private void BlitFromInternal(int sourceFboId, int srcWidth, int srcHeight,
        ClearBufferMask mask, BlitFramebufferFilter filter)
    {
        int prevFbo = GL.GetInteger(GetPName.FramebufferBinding);
        int prevReadFbo = GL.GetInteger(GetPName.ReadFramebufferBinding);
        int prevDrawFbo = GL.GetInteger(GetPName.DrawFramebufferBinding);
        int prevReadBuffer = GL.GetInteger(GetPName.ReadBuffer);
        int prevDrawBuffer = GL.GetInteger(GetPName.DrawBuffer);

        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, sourceFboId);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, fboId);

        // For color blits, make sure we read/write from the expected attachment.
        // VS primary FB is an MRT; without this, glBlitFramebuffer may copy the wrong attachment.
        if ((mask & ClearBufferMask.ColorBufferBit) != 0)
        {
            GL.ReadBuffer(sourceFboId == 0 ? ReadBufferMode.Back : ReadBufferMode.ColorAttachment0);
            GL.DrawBuffer(fboId == 0 ? DrawBufferMode.Back : DrawBufferMode.ColorAttachment0);
        }

        GL.BlitFramebuffer(
            0, 0, srcWidth, srcHeight,
            0, 0, Width, Height,
            mask,
            filter);

        // Restore previous bindings/state
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, prevReadFbo);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, prevDrawFbo);
        if ((mask & ClearBufferMask.ColorBufferBit) != 0)
        {
            GL.ReadBuffer((ReadBufferMode)prevReadBuffer);
            GL.DrawBuffer((DrawBufferMode)prevDrawBuffer);
        }
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, prevFbo);
    }

    private void BlitToInternal(int destFboId, int dstWidth, int dstHeight,
        ClearBufferMask mask, BlitFramebufferFilter filter)
    {
        int prevFbo = GL.GetInteger(GetPName.FramebufferBinding);
        int prevReadFbo = GL.GetInteger(GetPName.ReadFramebufferBinding);
        int prevDrawFbo = GL.GetInteger(GetPName.DrawFramebufferBinding);
        int prevReadBuffer = GL.GetInteger(GetPName.ReadBuffer);
        int prevDrawBuffer = GL.GetInteger(GetPName.DrawBuffer);

        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, fboId);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, destFboId);

        // For color blits, make sure we read/write from the expected attachment.
        // VS primary FB is an MRT; without this, glBlitFramebuffer may copy the wrong attachment.
        if ((mask & ClearBufferMask.ColorBufferBit) != 0)
        {
            GL.ReadBuffer(fboId == 0 ? ReadBufferMode.Back : ReadBufferMode.ColorAttachment0);
            GL.DrawBuffer(destFboId == 0 ? DrawBufferMode.Back : DrawBufferMode.ColorAttachment0);
        }

        GL.BlitFramebuffer(
            0, 0, Width, Height,
            0, 0, dstWidth, dstHeight,
            mask,
            filter);

        // Restore previous bindings/state
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, prevReadFbo);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, prevDrawFbo);
        if ((mask & ClearBufferMask.ColorBufferBit) != 0)
        {
            GL.ReadBuffer((ReadBufferMode)prevReadBuffer);
            GL.DrawBuffer((DrawBufferMode)prevDrawBuffer);
        }
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, prevFbo);
    }

    /// <summary>
    /// Saves the current framebuffer binding for later restoration.
    /// </summary>
    /// <returns>The currently bound framebuffer ID.</returns>
    public static int SaveBinding()
    {
        return GL.GetInteger(GetPName.FramebufferBinding);
    }

    /// <summary>
    /// Restores a previously saved framebuffer binding.
    /// </summary>
    /// <param name="fboId">The framebuffer ID to restore.</param>
    public static void RestoreBinding(int fboId)
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fboId);
    }

    #endregion

    #region Private Methods

    private void CreateFramebuffer()
    {
        fboId = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fboId);

#if DEBUG
        GlDebug.TryLabelFramebuffer(fboId, debugName);
#endif

        // Attach color textures
        var drawBuffers = new DrawBuffersEnum[colorAttachments.Count];
        for (int i = 0; i < colorAttachments.Count; i++)
        {
            var attachment = FramebufferAttachment.ColorAttachment0 + i;
            GL.FramebufferTexture2D(
                FramebufferTarget.Framebuffer,
                attachment,
                TextureTarget.Texture2D,
                colorAttachments[i].TextureId,
                0);
            drawBuffers[i] = DrawBuffersEnum.ColorAttachment0 + i;
        }

        // Set draw buffers
        if (colorAttachments.Count > 0)
        {
            GL.DrawBuffers(colorAttachments.Count, drawBuffers);
        }
        else
        {
            // Depth-only: disable color writes
            GL.DrawBuffer(DrawBufferMode.None);
            GL.ReadBuffer(ReadBufferMode.None);
        }

        // Attach depth texture if present
        if (depthAttachment != null)
        {
            var depthAttachmentType = TextureFormatHelper.IsDepthFormat(depthAttachment.InternalFormat)
                && depthAttachment.InternalFormat is PixelInternalFormat.Depth24Stencil8 
                    or PixelInternalFormat.Depth32fStencil8
                ? FramebufferAttachment.DepthStencilAttachment
                : FramebufferAttachment.DepthAttachment;

            GL.FramebufferTexture2D(
                FramebufferTarget.Framebuffer,
                depthAttachmentType,
                TextureTarget.Texture2D,
                depthAttachment.TextureId,
                0);
        }

        // Validate in debug mode
        if (!CheckStatus(out string? errorMessage))
        {
            System.Diagnostics.Debug.WriteLine($"[GBuffer] {errorMessage}");
        }

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Releases the GPU framebuffer resource.
    /// If ownsTextures was true during creation, also disposes attached textures.
    /// </summary>
    // Uses base GpuResource.Dispose(), with cleanup handled in OnAfterDelete().

    protected override void OnAfterDelete()
    {
        if (ownsTextures)
        {
            foreach (var texture in colorAttachments)
            {
                try { texture.Dispose(); } catch { }
            }

            try { depthAttachment?.Dispose(); } catch { }
        }

        colorAttachments.Clear();
        depthAttachment = null;
    }

    #endregion

    #region Implicit Conversion

    /// <summary>
    /// Allows implicit conversion to int for use with existing APIs expecting FBO IDs.
    /// </summary>
    public static implicit operator int(GpuFramebuffer buffer)
    {
        return buffer?.fboId ?? 0;
    }

    #endregion
}
