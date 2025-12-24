using System;

using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded;

/// <summary>
/// Renderer that attaches the G-buffer to the Primary framebuffer before chunk rendering.
/// Registered at EnumRenderStage.Before to ensure MRT is active when chunks draw.
/// </summary>
public sealed class GBufferRenderer : IRenderer, IDisposable
{
    private readonly ICoreClientAPI capi;
    private int normalTextureId;
    private int lastWidth;
    private int lastHeight;
    private bool isInitialized;

    /// <summary>
    /// The OpenGL texture ID for the normal G-buffer.
    /// </summary>
    public int NormalTextureId => normalTextureId;

    public double RenderOrder => 0.0; // Very early
    public int RenderRange => 1;

    public GBufferRenderer(ICoreClientAPI capi)
    {
        this.capi = capi;
        
        // Register at Before stage to set up MRT before chunks render
        capi.Event.RegisterRenderer(this, EnumRenderStage.Before, "gbuffer-setup");
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        var primaryFb = capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        int width = primaryFb.Width;
        int height = primaryFb.Height;

        // Check if we need to (re)create the texture due to size change
        if (!isInitialized || width != lastWidth || height != lastHeight)
        {
            CreateNormalTexture(width, height);
            AttachToFramebuffer(primaryFb.FboId);
            lastWidth = width;
            lastHeight = height;
        }

        // Ensure MRT is active each frame (in case something reset it)
        EnsureMRTActive(primaryFb.FboId);
    }

    private void CreateNormalTexture(int width, int height)
    {
        // Delete old texture if exists
        if (normalTextureId != 0)
        {
            GL.DeleteTexture(normalTextureId);
            normalTextureId = 0;
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

        // Attach our normal texture as ColorAttachment4
        // (0=outColor, 1=outGlow, 2=outGNormal, 3=outGPosition are used by the game)
        GL.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment4,
            TextureTarget.Texture2D,
            normalTextureId,
            0);

        // Verify framebuffer is complete
        var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != FramebufferErrorCode.FramebufferComplete)
        {
            capi.Logger.Error($"[VGE] Framebuffer incomplete after attaching G-buffer: {status}");
        }
        else
        {
            capi.Logger.Notification("[VGE] G-buffer normal texture attached to Primary framebuffer at ColorAttachment4");
        }

        // Unbind
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    private void EnsureMRTActive(int fboId)
    {
        // Bind and set draw buffers each frame
        // We need to include all attachments the game uses plus ours
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fboId);
        
        // Include all 5 attachments: 0=color, 1=glow, 2=gnormal, 3=gposition, 4=vge_normal
        DrawBuffersEnum[] drawBuffers = { 
            DrawBuffersEnum.ColorAttachment0, 
            DrawBuffersEnum.ColorAttachment1,
            DrawBuffersEnum.ColorAttachment2,
            DrawBuffersEnum.ColorAttachment3,
            DrawBuffersEnum.ColorAttachment4
        };
        GL.DrawBuffers(5, drawBuffers);
        
        // Don't unbind - leave it bound for subsequent rendering
    }

    public void Dispose()
    {
        // Detach from framebuffer
        var primaryFb = capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        if (primaryFb != null)
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, primaryFb.FboId);

            // Detach our texture from ColorAttachment4
            GL.FramebufferTexture2D(
                FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment4,
                TextureTarget.Texture2D,
                0,
                0);

            // Reset draw buffers to game defaults (depends on SSAO setting)
            DrawBuffersEnum[] drawBuffers = { 
                DrawBuffersEnum.ColorAttachment0,
                DrawBuffersEnum.ColorAttachment1
            };
            GL.DrawBuffers(2, drawBuffers);

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        if (normalTextureId != 0)
        {
            GL.DeleteTexture(normalTextureId);
            normalTextureId = 0;
        }

        capi.Logger.Notification("[VGE] G-buffer renderer disposed");
    }
}