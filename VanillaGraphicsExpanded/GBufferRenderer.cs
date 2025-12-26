using System;

using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded;

/// <summary>
/// Renderer that attaches G-buffer textures to the Primary framebuffer before chunk rendering.
/// Registered at EnumRenderStage.Before to ensure MRT is active when chunks draw.
/// 
/// G-buffer layout:
/// - ColorAttachment0: Original primary color (VS managed)
/// - ColorAttachment1: VS glow (if used)
/// - ColorAttachment2: VS gnormal (if SSAO enabled)
/// - ColorAttachment3: VS gposition (if SSAO enabled)
/// - ColorAttachment4: World-space normals (RGBA16F)
/// - ColorAttachment5: Material properties (RGBA16F) - Reflectivity, Roughness, Metallic, Emissive
/// - ColorAttachment6: Albedo (RGB8)
/// - DepthAttachment: Hyperbolic depth (Depth32f)
/// </summary>
public sealed class GBufferRenderer : IRenderer, IDisposable
{
    private readonly ICoreClientAPI capi;
    
    // Texture IDs for each G-buffer attachment
    private int normalTextureId;
    private int materialTextureId;
    private int albedoTextureId;
    private int hyperbolicDepthTextureId;
    
    private int lastWidth;
    private int lastHeight;
    private bool isInitialized;

    /// <summary>
    /// The OpenGL texture ID for the normal G-buffer (ColorAttachment4).
    /// Format: RGBA16F - World-space normals in XYZ, W = bevel strength.
    /// </summary>
    public int NormalTextureId => normalTextureId;

    /// <summary>
    /// The OpenGL texture ID for the material G-buffer (ColorAttachment5).
    /// Format: RGBA16F - (Reflectivity, Roughness, Metallic, Emissive).
    /// </summary>
    public int MaterialTextureId => materialTextureId;

    /// <summary>
    /// The OpenGL texture ID for the albedo G-buffer (ColorAttachment6).
    /// Format: RGB8 - Base color without lighting.
    /// </summary>
    public int AlbedoTextureId => albedoTextureId;

    /// <summary>
    /// The OpenGL texture ID for the hyperbolic depth G-buffer (DepthAttachment).
    /// Format: Depth32f - Hyperbolic depth value.
    /// </summary>
    public int HyperbolicDepthTextureId => hyperbolicDepthTextureId;

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

        // Check if we need to (re)create the textures due to size change
        if (!isInitialized || width != lastWidth || height != lastHeight)
        {
            CreateGBufferTextures(width, height);
            AttachToFramebuffer(primaryFb.FboId);
            lastWidth = width;
            lastHeight = height;
        }

        // Ensure MRT is active each frame (in case something reset it)
        EnsureMRTActive(primaryFb.FboId);
    }

    private void CreateGBufferTextures(int width, int height)
    {
        // Delete old textures if they exist
        DeleteTextures();

        // Create Normal texture (RGBA16F)
        normalTextureId = CreateColorTexture(width, height, PixelInternalFormat.Rgba16f, PixelFormat.Rgba, PixelType.Float);
        
        // Create Material texture (RGBA16F) - Reflectivity, Roughness, Metallic, Emissive
        materialTextureId = CreateColorTexture(width, height, PixelInternalFormat.Rgba16f, PixelFormat.Rgba, PixelType.Float);
        
        // Create Albedo texture (RGB8)
        albedoTextureId = CreateColorTexture(width, height, PixelInternalFormat.Rgb8, PixelFormat.Rgb, PixelType.UnsignedByte);
        
        // Create Hyperbolic Depth texture (Depth32f)
        hyperbolicDepthTextureId = CreateDepthTexture(width, height);

        isInitialized = true;
        capi.Logger.Notification($"[VGE] Created G-buffer textures: {width}x{height}");
        capi.Logger.Notification($"[VGE]   Normal ID={normalTextureId}, Material ID={materialTextureId}, Albedo ID={albedoTextureId}, HyperbolicDepth ID={hyperbolicDepthTextureId}");
    }

    private int CreateColorTexture(int width, int height, PixelInternalFormat internalFormat, PixelFormat format, PixelType type)
    {
        int textureId = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, textureId);

        GL.TexImage2D(
            TextureTarget.Texture2D,
            0,
            internalFormat,
            width,
            height,
            0,
            format,
            type,
            IntPtr.Zero);

        // Set filtering (nearest for G-buffer data)
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        GL.BindTexture(TextureTarget.Texture2D, 0);
        return textureId;
    }

    private int CreateDepthTexture(int width, int height)
    {
        int textureId = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, textureId);

        GL.TexImage2D(
            TextureTarget.Texture2D,
            0,
            PixelInternalFormat.DepthComponent32f,
            width,
            height,
            0,
            PixelFormat.DepthComponent,
            PixelType.Float,
            IntPtr.Zero);

        // Set filtering (nearest for depth data)
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        // Prevent depth comparison when sampling
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureCompareMode, (int)TextureCompareMode.None);

        GL.BindTexture(TextureTarget.Texture2D, 0);
        return textureId;
    }

    private void DeleteTextures()
    {
        if (normalTextureId != 0)
        {
            GL.DeleteTexture(normalTextureId);
            normalTextureId = 0;
        }
        if (materialTextureId != 0)
        {
            GL.DeleteTexture(materialTextureId);
            materialTextureId = 0;
        }
        if (albedoTextureId != 0)
        {
            GL.DeleteTexture(albedoTextureId);
            albedoTextureId = 0;
        }
        if (hyperbolicDepthTextureId != 0)
        {
            GL.DeleteTexture(hyperbolicDepthTextureId);
            hyperbolicDepthTextureId = 0;
        }
    }

    private void AttachToFramebuffer(int fboId)
    {
        // Bind the Primary framebuffer
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fboId);

        // Attach our textures after VS attachments (0=outColor, 1=outGlow, 2=outGNormal, 3=outGPosition)
        
        // Attach normal texture as ColorAttachment4
        GL.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment4,
            TextureTarget.Texture2D,
            normalTextureId,
            0);

        // Attach material texture as ColorAttachment5
        GL.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment5,
            TextureTarget.Texture2D,
            materialTextureId,
            0);

        // Attach albedo texture as ColorAttachment6
        GL.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment6,
            TextureTarget.Texture2D,
            albedoTextureId,
            0);

        // Attach hyperbolic depth texture as DepthAttachment (replaces VS default depth)
        GL.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.DepthAttachment,
            TextureTarget.Texture2D,
            hyperbolicDepthTextureId,
            0);

        // Verify framebuffer is complete
        var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != FramebufferErrorCode.FramebufferComplete)
        {
            capi.Logger.Error($"[VGE] Framebuffer incomplete after attaching G-buffer: {status}");
        }
        else
        {
            capi.Logger.Notification("[VGE] G-buffer textures attached to Primary framebuffer (Normal@4, Material@5, Albedo@6, Depth)");
        }

        // Unbind
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    private void EnsureMRTActive(int fboId)
    {
        // Bind and set draw buffers each frame
        // We need to include all attachments the game uses plus ours
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fboId);
        
        // Include all 7 color attachments: 
        // 0=color, 1=glow, 2=gnormal, 3=gposition, 4=vge_normal, 5=vge_material, 6=vge_albedo
        // Note: Depth attachment is not included in draw buffers (handled automatically)
        DrawBuffersEnum[] drawBuffers = { 
            DrawBuffersEnum.ColorAttachment0, 
            DrawBuffersEnum.ColorAttachment1,
            DrawBuffersEnum.ColorAttachment2,
            DrawBuffersEnum.ColorAttachment3,
            DrawBuffersEnum.ColorAttachment4,
            DrawBuffersEnum.ColorAttachment5,
            DrawBuffersEnum.ColorAttachment6
        };
        GL.DrawBuffers(7, drawBuffers);
        
        // Don't unbind - leave it bound for subsequent rendering
    }

    public void Dispose()
    {
        // Detach from framebuffer
        var primaryFb = capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        if (primaryFb != null)
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, primaryFb.FboId);

            // Detach our color textures
            GL.FramebufferTexture2D(
                FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment4,
                TextureTarget.Texture2D,
                0,
                0);

            GL.FramebufferTexture2D(
                FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment5,
                TextureTarget.Texture2D,
                0,
                0);

            GL.FramebufferTexture2D(
                FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment6,
                TextureTarget.Texture2D,
                0,
                0);

            // Note: We don't detach DepthAttachment as VS manages the original depth buffer
            // and will need to reattach it. The depth texture will be deleted below.

            // Reset draw buffers to game defaults (depends on SSAO setting)
            DrawBuffersEnum[] drawBuffers = { 
                DrawBuffersEnum.ColorAttachment0,
                DrawBuffersEnum.ColorAttachment1
            };
            GL.DrawBuffers(2, drawBuffers);

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        DeleteTextures();

        capi.Logger.Notification("[VGE] G-buffer renderer disposed");
    }
}