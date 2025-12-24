using System;
using Vintagestory.API.Client;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded;

/// <summary>
/// Manages a G-buffer normal attachment for the Primary framebuffer using raw OpenGL calls.
/// This adds a second color attachment (GL_COLOR_ATTACHMENT1) to store world-space normals.
/// </summary>
public sealed class GBufferManager : IDisposable
{
    private readonly ICoreClientAPI capi;
    private int normalTextureId;
    private int lastWidth;
    private int lastHeight;
    private bool isInitialized;
    private bool isAttached;

    /// <summary>
    /// The OpenGL texture ID for the normal G-buffer.
    /// </summary>
    public int NormalTextureId => normalTextureId;

    /// <summary>
    /// Whether the G-buffer is successfully attached to the Primary framebuffer.
    /// </summary>
    public bool IsAttached => isAttached;

    public GBufferManager(ICoreClientAPI capi)
    {
        this.capi = capi;
    }

    /// <summary>
    /// Ensures the G-buffer normal texture exists and is attached to the Primary framebuffer.
    /// Call this before rendering each frame.
    /// </summary>
    public void EnsureAttached()
    {
        var primaryFb = capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        int width = primaryFb.Width;
        int height = primaryFb.Height;

        // Check if we need to (re)create the texture due to size change
        if (!isInitialized || width != lastWidth || height != lastHeight)
        {
            CreateNormalTexture(width, height);
            lastWidth = width;
            lastHeight = height;
        }

        // Attach to framebuffer if not already attached
        if (!isAttached && normalTextureId != 0)
        {
            AttachToFramebuffer(primaryFb.FboId);
        }
    }

    private void CreateNormalTexture(int width, int height)
    {
        // Delete old texture if exists
        if (normalTextureId != 0)
        {
            GL.DeleteTexture(normalTextureId);
            normalTextureId = 0;
            isAttached = false;
        }

        // Generate new texture
        normalTextureId = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, normalTextureId);

        // Allocate storage - Rgba16f for high precision normals
        GL.TexImage2D(
            TextureTarget.Texture2D,
            0,
            PixelInternalFormat.Rgba16f,
            width,
            height,
            0,
            PixelFormat.Rgba,
            PixelType.Float,
            IntPtr.Zero);

        // Set filtering (nearest for G-buffer data)
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        GL.BindTexture(TextureTarget.Texture2D, 0);

        isInitialized = true;
        capi.Logger.Notification($"[VGE] Created G-buffer normal texture: {width}x{height}, ID={normalTextureId}");
    }

    private void AttachToFramebuffer(int fboId)
    {
        // Bind the Primary framebuffer
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fboId);

        // Attach our normal texture as ColorAttachment1
        GL.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment1,
            TextureTarget.Texture2D,
            normalTextureId,
            0);

        // Update draw buffers to include both attachments
        DrawBuffersEnum[] drawBuffers = { 
            DrawBuffersEnum.ColorAttachment0, 
            DrawBuffersEnum.ColorAttachment1 
        };
        GL.DrawBuffers(2, drawBuffers);

        // Verify framebuffer is complete
        var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != FramebufferErrorCode.FramebufferComplete)
        {
            capi.Logger.Error($"[VGE] Framebuffer incomplete after attaching G-buffer: {status}");
            isAttached = false;
        }
        else
        {
            capi.Logger.Notification("[VGE] G-buffer normal texture attached to Primary framebuffer");
            isAttached = true;
        }

        // Unbind framebuffer
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    /// <summary>
    /// Detaches the normal texture from the Primary framebuffer.
    /// Call this before disposing or when the G-buffer is no longer needed.
    /// </summary>
    public void Detach()
    {
        if (!isAttached) return;

        var primaryFb = capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, primaryFb.FboId);

        // Detach our texture
        GL.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment1,
            TextureTarget.Texture2D,
            0,
            0);

        // Reset draw buffers to only ColorAttachment0
        DrawBuffersEnum[] drawBuffers = { DrawBuffersEnum.ColorAttachment0 };
        GL.DrawBuffers(1, drawBuffers);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

        isAttached = false;
        capi.Logger.Notification("[VGE] G-buffer detached from Primary framebuffer");
    }

    public void Dispose()
    {
        Detach();

        if (normalTextureId != 0)
        {
            GL.DeleteTexture(normalTextureId);
            normalTextureId = 0;
        }

        isInitialized = false;
    }
}
