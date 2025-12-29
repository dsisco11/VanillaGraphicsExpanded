using System;
using Vintagestory.API.Client;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded;

/// <summary>
/// Manages G-buffer attachments for the Primary framebuffer using raw OpenGL calls.
/// This adds multiple color attachments to store deferred rendering data:
/// - ColorAttachment0-3: Managed by VS (outColor/Albedo, outGlow, outGNormal, outGPosition)
/// - ColorAttachment4: World-space normals (RGBA16F) - layout(location = 4)
/// - ColorAttachment5: Material properties (RGBA16F) - layout(location = 5)
/// 
/// Integrates with VS via Harmony hooks for framebuffer lifecycle management.
/// </summary>
public sealed class GBufferManager : IDisposable
{
    #region Static Instance
    
    /// <summary>
    /// Singleton instance accessible from Harmony hooks.
    /// </summary>
    public static GBufferManager? Instance { get; private set; }
    
    #endregion

    #region Fields
    
    private readonly ICoreClientAPI capi;
    
    // Texture IDs for each G-buffer attachment
    private int normalTextureId;
    private int materialTextureId;
    private int normalSlotId = 4;
    private int materialSlotId = 5;
    private float[] clearColor = [0f, 0f, 0f, 0f];

    private int lastWidth;
    private int lastHeight;
    /// <summary>
    /// Whether the G-buffer textures have been created and are ready for attachment.
    /// </summary>
    private bool isInitialized = false;

    /// <summary>
    /// Whether the G-buffer textures have been injected into the framebuffer array.
    /// </summary>
    private bool isInjected = false;

    #endregion

    #region Properties

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
    /// Whether the G-buffer textures have been created and are ready for attachment.
    /// </summary>
    public bool IsInitialized => isInitialized;
    
    #endregion

    #region Constructor / Destructor

    public GBufferManager(ICoreClientAPI capi)
    {
        this.capi = capi;
        Instance = this;
    }
    
    #endregion

    #region Harmony Hook Methods
    
    /// <summary>
    /// Called by Harmony hook when VS sets up default framebuffers.
    /// Creates and attaches G-buffer textures to the Primary framebuffer.
    /// </summary>
    public void SetupGBuffers()
    {
        FrameBufferRef? primaryFb = capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        if (primaryFb is null)
        {
            capi.Logger.Error("[VGE] Primary framebuffer not found during G-buffer setup.");
            return;
        }
        int width = primaryFb.Width;
        int height = primaryFb.Height;

        // Create textures if needed or if size changed
        if (!isInitialized || width != lastWidth || height != lastHeight)
        {
            CreateGBufferTextures(width, height);
            lastWidth = width;
            lastHeight = height;
        }

        // Inject our textures into the framebuffers array
        // Need to expand the ColorTextureIds array to hold our attachments
        primaryFb.ColorTextureIds = [..primaryFb.ColorTextureIds, normalTextureId, materialTextureId];
        isInjected = true;

        // Attach to the Primary framebuffer
        AttachToFramebuffer(primaryFb.FboId);
    }

    /// <summary>
    /// Called by Harmony hook when VS loads (binds) a framebuffer.
    /// Ensures MRT draw buffers are set correctly when Primary is loaded.
    /// </summary>
    /// <param name="framebuffer">The framebuffer being loaded</param>
    public void LoadGBuffer(EnumFrameBuffer framebuffer)
    {
        if (framebuffer != EnumFrameBuffer.Primary)
            return;

        if (!isInitialized || !isInjected)
        {
            SetupGBuffers();
        }

        if (!isInitialized || !isInjected)
            return;

        // Set draw buffers to include our attachments
        // VS only sets 0-3, we need to add 4-5
        DrawBuffersEnum[] drawBuffers = [ 
            DrawBuffersEnum.ColorAttachment0,  // VS: outColor (Albedo)
            DrawBuffersEnum.ColorAttachment1,  // VS: outGlow
            DrawBuffersEnum.ColorAttachment2,  // VS: outGNormal (SSAO)
            DrawBuffersEnum.ColorAttachment3,  // VS: outGPosition (SSAO)
            DrawBuffersEnum.ColorAttachment4,  // VGE: Normal
            DrawBuffersEnum.ColorAttachment5   // VGE: Material
        ];
        GL.DrawBuffers(6, drawBuffers);        
        // Disable blending for VGE attachments to ensure direct writes
        GL.BlendFunc(normalSlotId, BlendingFactorSrc.One, BlendingFactorDest.Zero);
        GL.BlendFunc(materialSlotId, BlendingFactorSrc.One, BlendingFactorDest.Zero);
        //GL.Disable(IndexedEnableCap.Blend, normalSlotId);
        //GL.Disable(IndexedEnableCap.Blend, materialSlotId);
    }

    /// <summary>
    /// Called by Harmony hook when VS clears a framebuffer.
    /// Clears our G-buffer attachments when Primary is cleared.
    /// </summary>
    /// <param name="framebuffer">The framebuffer being cleared</param>
    public void ClearGBuffer(EnumFrameBuffer framebuffer)
    {
        if (!isInitialized || !isInjected)
            return;

        if (framebuffer == EnumFrameBuffer.Primary)
        {
            // Clear our G-buffer attachments to default values
            // Using glClearBuffer to clear individual attachments without affecting VS attachments
            
            // Clear normal buffer (attachment 4) to (0, 0, 0, 0) - no normal data
            GL.ClearBuffer(ClearBuffer.Color, normalSlotId, clearColor);
            
            // Clear material buffer (attachment 5) to (0, 0, 0, 0) - no material data
            GL.ClearBuffer(ClearBuffer.Color, materialSlotId, clearColor);
        }
    }

    /// <summary>
    /// Called by Harmony hook when VS unloads a framebuffer.
    /// Used for returning the GL state to default if needed.
    /// </summary>
    /// <param name="framebuffer">The framebuffer being unloaded</param>
    public void UnloadGBuffer(EnumFrameBuffer framebuffer)
    {
        //if (framebuffer != EnumFrameBuffer.Primary || !isInitialized || !isInjected)
        //    return;
    }

    #endregion

    #region Private Methods

    private void CreateGBufferTextures(int width, int height)
    {
        // Delete old textures if they exist
        DeleteTextures();

        // Create Normal texture (RGBA16F)
        normalTextureId = CreateTexture(width, height, PixelInternalFormat.Rgba16f, PixelFormat.Rgba, PixelType.Float);

        // Create Material texture (RGBA8) - Roughness, Metallic, Emissive, Reflectivity
        materialTextureId = CreateTexture(width, height, PixelInternalFormat.Rgba8, PixelFormat.Rgba, PixelType.UnsignedByte);

        isInitialized = true;
        capi.Logger.Notification($"[VGE] Created G-buffer textures: {width}x{height}");
        capi.Logger.Notification($"[VGE]   Normal ID={normalTextureId}, Material ID={materialTextureId}");
    }

    private static int CreateTexture(int width, int height, PixelInternalFormat internalFormat, PixelFormat format, PixelType type)
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
    }

    /// <summary>
    /// Attaches the G-buffer textures to the specified framebuffer.
    /// </summary>
    /// <param name="fboId"></param>
    private void AttachToFramebuffer(int fboId)
    {
        // stash the currently bound framebuffer
        var prevFbo = GL.GetInteger(GetPName.FramebufferBinding);
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

        // Disable blending for VGE attachments to ensure direct writes
        GL.BlendFunc(normalSlotId, BlendingFactorSrc.One, BlendingFactorDest.Zero);
        GL.BlendFunc(materialSlotId, BlendingFactorSrc.One, BlendingFactorDest.Zero);
        GL.Disable(IndexedEnableCap.Blend, normalSlotId);
        GL.Disable(IndexedEnableCap.Blend, materialSlotId);

        // Verify framebuffer is complete
        var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != FramebufferErrorCode.FramebufferComplete)
        {
            capi.Logger.Error($"[VGE] Framebuffer incomplete after attaching G-buffer: {status}");
        }
        else
        {
            capi.Logger.Notification("[VGE] G-buffer textures attached to Primary framebuffer (Normal@4, Material@5)");
        }

        // Restore previous framebuffer binding
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, prevFbo);
    }

    /// <summary>
    /// Detaches the G-buffer textures from the specified framebuffer.
    /// </summary>
    /// <param name="fboId"></param>
    private void DetachFromFramebuffer(int fboId)
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fboId);

        // Detach our color textures (ColorAttachment4-5)
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

        // Reset draw buffers to VS defaults (0-3)
        DrawBuffersEnum[] drawBuffers = { 
            DrawBuffersEnum.ColorAttachment0,
            DrawBuffersEnum.ColorAttachment1,
            DrawBuffersEnum.ColorAttachment2,
            DrawBuffersEnum.ColorAttachment3
        };
        GL.DrawBuffers(4, drawBuffers);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        
        capi.Logger.Notification("[VGE] G-buffer detached from Primary framebuffer");
    }
    
    #endregion

    #region IDisposable

    public void Dispose()
    {
        // Clean up textures (framebuffer attachment cleanup happens via UnloadGBuffer hook)
        DeleteTextures();
        isInitialized = false;
        
        // Clear the static instance
        if (Instance == this)
        {
            Instance = null;
        }
    }
    
    #endregion
}
