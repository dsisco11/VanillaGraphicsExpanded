using System;
using Vintagestory.API.Client;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded;

/// <summary>
/// Manages G-buffer attachments for the Primary framebuffer using raw OpenGL calls.
/// This adds multiple color attachments to store deferred rendering data:
/// - ColorAttachment0-3: Managed by VS (outColor, outGlow, outGNormal, outGPosition)
/// - ColorAttachment4: World-space normals (RGBA16F) - layout(location = 4)
/// - ColorAttachment5: Material properties (RGBA16F) - layout(location = 5)
/// - ColorAttachment6: Albedo (RGB8) - layout(location = 6)
/// - DepthAttachment: Hyperbolic depth (Depth32f)
/// </summary>
public sealed class GBufferManager : IDisposable
{
    private readonly ICoreClientAPI capi;
    
    // Texture IDs for each G-buffer attachment
    private int normalTextureId;
    private int materialTextureId;
    private int hyperbolicDepthTextureId;
    private int albedoTextureId;
    
    private int lastWidth;
    private int lastHeight;
    private bool isInitialized;
    private bool isAttached;

    /// <summary>
    /// The OpenGL texture ID for the normal G-buffer (ColorAttachment1).
    /// Format: RGBA16F - World-space normals in XYZ, W unused.
    /// </summary>
    public int NormalTextureId => normalTextureId;

    /// <summary>
    /// The OpenGL texture ID for the material G-buffer (ColorAttachment2).
    /// Format: RGBA16F - (Reflectivity, Roughness, Metallic, Emissive).
    /// </summary>
    public int MaterialTextureId => materialTextureId;

    /// <summary>
    /// The OpenGL texture ID for the hyperbolic depth G-buffer (DepthAttachment).
    /// Format: Depth32f - Hyperbolic depth value.
    /// </summary>
    public int HyperbolicDepthTextureId => hyperbolicDepthTextureId;

    /// <summary>
    /// The OpenGL texture ID for the albedo G-buffer (ColorAttachment3).
    /// Format: RGB8 - Base color without lighting.
    /// </summary>
    public int AlbedoTextureId => albedoTextureId;

    /// <summary>
    /// Whether the G-buffer is successfully attached to the Primary framebuffer.
    /// </summary>
    public bool IsAttached => isAttached;

    public GBufferManager(ICoreClientAPI capi)
    {
        this.capi = capi;
    }

    /// <summary>
    /// Ensures the G-buffer textures exist and are attached to the Primary framebuffer.
    /// Call this before rendering each frame.
    /// </summary>
    public void EnsureAttached()
    {
        var primaryFb = capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        int width = primaryFb.Width;
        int height = primaryFb.Height;

        // Check if we need to (re)create the textures due to size change
        if (!isInitialized || width != lastWidth || height != lastHeight)
        {
            CreateGBufferTextures(width, height);
            lastWidth = width;
            lastHeight = height;
        }

        // Attach to framebuffer if not already attached
        if (!isAttached && normalTextureId != 0)
        {
            AttachToFramebuffer(primaryFb.FboId);
        }
    }

    private void CreateGBufferTextures(int width, int height)
    {
        // Delete old textures if they exist
        DeleteTextures();

        // Create Normal texture (RGBA16F)
        normalTextureId = CreateTexture(width, height, PixelInternalFormat.Rgba16f, PixelFormat.Rgba, PixelType.Float);
        
        // Create Material texture (RGBA16F) - Reflectivity, Roughness, Metallic, Emissive
        materialTextureId = CreateTexture(width, height, PixelInternalFormat.Rgba16f, PixelFormat.Rgba, PixelType.Float);
        
        // Create Albedo texture (RGB8)
        albedoTextureId = CreateTexture(width, height, PixelInternalFormat.Rgb8, PixelFormat.Rgb, PixelType.UnsignedByte);
        
        // Create Hyperbolic Depth texture (Depth32f) - actual depth attachment
        hyperbolicDepthTextureId = CreateDepthTexture(width, height);

        isInitialized = true;
        capi.Logger.Notification($"[VGE] Created G-buffer textures: {width}x{height}");
        capi.Logger.Notification($"[VGE]   Normal ID={normalTextureId}, Material ID={materialTextureId}, HyperbolicDepth ID={hyperbolicDepthTextureId}, Albedo ID={albedoTextureId}");
    }

    private int CreateTexture(int width, int height, PixelInternalFormat internalFormat, PixelFormat format, PixelType type)
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
        if (hyperbolicDepthTextureId != 0)
        {
            GL.DeleteTexture(hyperbolicDepthTextureId);
            hyperbolicDepthTextureId = 0;
        }
        if (albedoTextureId != 0)
        {
            GL.DeleteTexture(albedoTextureId);
            albedoTextureId = 0;
        }
        isAttached = false;
    }

    private void AttachToFramebuffer(int fboId)
    {
        // Bind the Primary framebuffer
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fboId);

        // Attach normal texture as ColorAttachment4 (matches layout(location = 4))
        GL.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment4,
            TextureTarget.Texture2D,
            normalTextureId,
            0);

        // Attach material texture as ColorAttachment5 (matches layout(location = 5))
        GL.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment5,
            TextureTarget.Texture2D,
            materialTextureId,
            0);

        // Attach albedo texture as ColorAttachment6 (matches layout(location = 6))
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

        // Update draw buffers to include all color attachments (depth is not included)
        // Must include all attachments 0-6, even VS-managed ones, for MRT to work correctly
        DrawBuffersEnum[] drawBuffers = { 
            DrawBuffersEnum.ColorAttachment0,  // VS: outColor
            DrawBuffersEnum.ColorAttachment1,  // VS: outGlow
            DrawBuffersEnum.ColorAttachment2,  // VS: outGNormal (SSAO)
            DrawBuffersEnum.ColorAttachment3,  // VS: outGPosition (SSAO)
            DrawBuffersEnum.ColorAttachment4,  // VGE: Normal
            DrawBuffersEnum.ColorAttachment5,  // VGE: Material
            DrawBuffersEnum.ColorAttachment6   // VGE: Albedo
        };
        GL.DrawBuffers(7, drawBuffers);

        // Verify framebuffer is complete
        var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != FramebufferErrorCode.FramebufferComplete)
        {
            capi.Logger.Error($"[VGE] Framebuffer incomplete after attaching G-buffer: {status}");
            isAttached = false;
        }
        else
        {
            capi.Logger.Notification("[VGE] G-buffer textures attached to Primary framebuffer (Normal, Material, HyperbolicDepth, Albedo)");
            isAttached = true;
        }

        // Unbind framebuffer
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    /// <summary>
    /// Detaches all G-buffer textures from the Primary framebuffer.
    /// Call this before disposing or when the G-buffer is no longer needed.
    /// </summary>
    public void Detach()
    {
        if (!isAttached) return;

        var primaryFb = capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, primaryFb.FboId);

        // Detach all our textures (ColorAttachment4-6)
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
        // and will need to reattach it. The depth texture will be deleted on dispose.

        // Reset draw buffers to VS defaults (0-3)
        DrawBuffersEnum[] drawBuffers = { 
            DrawBuffersEnum.ColorAttachment0,
            DrawBuffersEnum.ColorAttachment1,
            DrawBuffersEnum.ColorAttachment2,
            DrawBuffersEnum.ColorAttachment3
        };
        GL.DrawBuffers(4, drawBuffers);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

        isAttached = false;
        capi.Logger.Notification("[VGE] G-buffer detached from Primary framebuffer");
    }

    public void Dispose()
    {
        Detach();
        DeleteTextures();
        isInitialized = false;
    }
}
