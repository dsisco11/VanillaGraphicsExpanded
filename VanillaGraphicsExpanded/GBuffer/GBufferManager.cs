using System;
using Vintagestory.API.Client;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

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
    
    // G-buffer textures using DynamicTexture
    private DynamicTexture2D? normalTex;
    private DynamicTexture2D? materialTex;

    private const int NormalSlotId = 4;
    private const int MaterialSlotId = 5;
    private readonly float[] clearColor = [0f, 0f, 0f, 0f];

    private static readonly GlPipelineDesc GBufferBlendPso = new(
        defaultMask: default(GlPipelineStateMask)
            .With(GlPipelineStateId.BlendEnableIndexed)
            .With(GlPipelineStateId.BlendFuncIndexed),
        nonDefaultMask: default,
        blendEnableIndexedAttachments: [(byte)NormalSlotId, (byte)MaterialSlotId],
        blendFuncIndexed:
        [
            new GlBlendFuncIndexed((byte)NormalSlotId, GlBlendFunc.Default),
            new GlBlendFuncIndexed((byte)MaterialSlotId, GlBlendFunc.Default)
        ]);

    private int lastWidth;
    private int lastHeight;
    
    /// <summary>
    /// Whether the G-buffer textures have been created and are ready for attachment.
    /// </summary>
    private bool isInitialized;

    /// <summary>
    /// Whether the G-buffer textures have been injected into the framebuffer array.
    /// </summary>
    private bool isInjected;

    #endregion

    #region Properties

    /// <summary>
    /// The OpenGL texture ID for the normal G-buffer (ColorAttachment4).
    /// Format: RGBA16F - World-space normals in XYZ, W = bevel strength.
    /// </summary>
    public int NormalTextureId => normalTex?.TextureId ?? 0;

    /// <summary>
    /// The OpenGL texture ID for the material G-buffer (ColorAttachment5).
    /// Format: RGBA16F - (Roughness, Metallic, Emissive, Reflectivity).
    /// </summary>
    public int MaterialTextureId => materialTex?.TextureId ?? 0;

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

        // Label VS framebuffer and textures for debugging
        LabelVintageStoryFramebuffer(primaryFb);

        // Inject our textures into the framebuffers array
        // Need to expand the ColorTextureIds array to hold our attachments
        primaryFb.ColorTextureIds = [..primaryFb.ColorTextureIds, NormalTextureId, MaterialTextureId];
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
        
        // Per-buffer blend control requires GL 4.0+ / ARB_draw_buffers_blend
        ApplyGBufferBlendState(forceDirty: true);

        // Verify the blend state was actually set
        VerifyBlendState();
    }

    /// <summary>
    /// Applies the correct blend state for G-buffer attachments (One/Zero, disabled).
    /// </summary>
    private void ApplyGBufferBlendState(bool forceDirty)
    {
        var gl = GlStateCache.Current;

        if (forceDirty)
        {
            // Engine/global glBlendFunc calls can stomp indexed blend factors. Mark them dirty so the cache re-emits.
            gl.DirtyIndexedBlendFunc();
            gl.DirtyIndexedBlendEnable();
        }

        gl.Apply(GBufferBlendPso);
    }

    /// <summary>
    /// Called by Harmony hook after VS sets global blend state via GlToggleBlend.
    /// Reapplies G-buffer blend state that was overwritten by global GL.BlendFunc.
    /// </summary>
    public void ReapplyGBufferBlendState()
    {
        if (!isInitialized || !isInjected)
            return;

        // Only reapply if Primary framebuffer is currently bound
        int currentFbo = GL.GetInteger(GetPName.FramebufferBinding);
        FrameBufferRef? primaryFb = capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        if (primaryFb is null || currentFbo != primaryFb.FboId)
            return;

        ApplyGBufferBlendState(forceDirty: true);
    }

    private bool hasLoggedBlendState;
    private void VerifyBlendState()
    {
        if (hasLoggedBlendState) return;
        hasLoggedBlendState = true;

        // Check if blend is enabled/disabled for each buffer
        bool blend4Enabled = GL.IsEnabled(IndexedEnableCap.Blend, NormalSlotId);
        bool blend5Enabled = GL.IsEnabled(IndexedEnableCap.Blend, MaterialSlotId);
        
        // Also check VS buffers for comparison
        bool blend2Enabled = GL.IsEnabled(IndexedEnableCap.Blend, 2);
        bool blend3Enabled = GL.IsEnabled(IndexedEnableCap.Blend, 3);

        // Query blend func using raw GL constants
        // GL_BLEND_SRC_RGB = 0x80C9, GL_BLEND_DST_RGB = 0x80C8
        const int GL_BLEND_SRC_RGB = 0x80C9;
        const int GL_BLEND_DST_RGB = 0x80C8;
        
        GL.GetInteger((GetIndexedPName)GL_BLEND_SRC_RGB, NormalSlotId, out int srcRgb4);
        GL.GetInteger((GetIndexedPName)GL_BLEND_DST_RGB, NormalSlotId, out int dstRgb4);
        GL.GetInteger((GetIndexedPName)GL_BLEND_SRC_RGB, MaterialSlotId, out int srcRgb5);
        GL.GetInteger((GetIndexedPName)GL_BLEND_DST_RGB, MaterialSlotId, out int dstRgb5);
        
        // Compare with VS buffers
        GL.GetInteger((GetIndexedPName)GL_BLEND_SRC_RGB, 2, out int srcRgb2);
        GL.GetInteger((GetIndexedPName)GL_BLEND_DST_RGB, 2, out int dstRgb2);
        GL.GetInteger((GetIndexedPName)GL_BLEND_SRC_RGB, 3, out int srcRgb3);
        GL.GetInteger((GetIndexedPName)GL_BLEND_DST_RGB, 3, out int dstRgb3);

        // GL_ONE = 1, GL_ZERO = 0
        capi.Logger.Notification($"[VGE] Blend state verification:");
        capi.Logger.Notification($"[VGE]   Buffer 2 (VS GNormal):   Enabled={blend2Enabled}, Src={srcRgb2}, Dst={dstRgb2}");
        capi.Logger.Notification($"[VGE]   Buffer 3 (VS GPosition): Enabled={blend3Enabled}, Src={srcRgb3}, Dst={dstRgb3}");
        capi.Logger.Notification($"[VGE]   Buffer 4 (VGE Normal):   Enabled={blend4Enabled}, Src={srcRgb4}, Dst={dstRgb4}");
        capi.Logger.Notification($"[VGE]   Buffer 5 (VGE Material): Enabled={blend5Enabled}, Src={srcRgb5}, Dst={dstRgb5}");
        capi.Logger.Notification($"[VGE]   (GL_ONE=1, GL_ZERO=0, GL_SRC_ALPHA=770, GL_ONE_MINUS_SRC_ALPHA=771)");
        
        // Check for GL errors
        var error = GL.GetError();
        if (error != ErrorCode.NoError)
        {
            capi.Logger.Warning($"[VGE] GL Error after blend state query: {error}");
        }
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
            GL.ClearBuffer(ClearBuffer.Color, NormalSlotId, clearColor);
            
            // Clear material buffer (attachment 5) to (0, 0, 0, 0) - no material data
            GL.ClearBuffer(ClearBuffer.Color, MaterialSlotId, clearColor);
        }
    }

    /// <summary>
    /// Called by Harmony hook when VS unloads a framebuffer.
    /// Used for returning the GL state to default if needed.
    /// </summary>
    /// <param name="framebuffer">The framebuffer being unloaded</param>
    public void UnloadGBuffer(EnumFrameBuffer framebuffer)
    {
        // Currently no cleanup needed on unload
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Ensures G-buffer textures exist and match the current screen size.
    /// Call this before rendering to handle resize events that may have
    /// invalidated the textures between Harmony hook calls and render time.
    /// </summary>
    /// <param name="screenWidth">Current screen width</param>
    /// <param name="screenHeight">Current screen height</param>
    /// <returns>True if textures are valid and ready to use</returns>
    public bool EnsureBuffers(int screenWidth, int screenHeight)
    {
        // Check if textures need to be (re)created
        if (!isInitialized || screenWidth != lastWidth || screenHeight != lastHeight)
        {
            // Get the primary framebuffer to attach to
            FrameBufferRef? primaryFb = capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
            if (primaryFb is null)
            {
                return false;
            }

            // Create new textures
            CreateGBufferTextures(screenWidth, screenHeight);
            lastWidth = screenWidth;
            lastHeight = screenHeight;

            // Re-attach to framebuffer
            AttachToFramebuffer(primaryFb.FboId);
            
            // Update the ColorTextureIds array if not already done
            if (!isInjected)
            {
                primaryFb.ColorTextureIds = [..primaryFb.ColorTextureIds, NormalTextureId, MaterialTextureId];
                isInjected = true;
            }

            capi.Logger.Debug($"[VGE] EnsureBuffers: Recreated G-buffer textures for {screenWidth}x{screenHeight}");
        }

        // Return true only if we have valid texture IDs
        return isInitialized && NormalTextureId != 0 && MaterialTextureId != 0;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Labels the VS primary framebuffer and its textures for easier debugging.
    /// </summary>
    private void LabelVintageStoryFramebuffer(FrameBufferRef fb)
    {
#if DEBUG
        // Label the framebuffer itself
        GlDebug.TryLabelFramebuffer(fb.FboId, "VS_Primary");

        // Label color attachments
        if (fb.ColorTextureIds != null)
        {
            for (int i = 0; i < fb.ColorTextureIds.Length && i < 4; i++)
            {
                string texName = i switch
                {
                    0 => "VS_outColor",
                    1 => "VS_outGlow",
                    2 => "VS_outGNormal",
                    3 => "VS_outGPosition",
                    _ => $"VS_Color{i}"
                };
                GlDebug.TryLabel(ObjectLabelIdentifier.Texture, fb.ColorTextureIds[i], texName);
            }
        }

        // Label depth attachment
        if (fb.DepthTextureId != 0)
        {
            GlDebug.TryLabel(ObjectLabelIdentifier.Texture, fb.DepthTextureId, "VS_Depth");
        }
#endif
    }

    private void CreateGBufferTextures(int width, int height)
    {
        // Delete old textures if they exist
        DeleteTextures();

        // Create Normal texture (RGBA16F)
        normalTex = DynamicTexture2D.Create(width, height, PixelInternalFormat.Rgba16f, debugName: "gNormal");
        ConfigureAsNonMipRenderTarget(normalTex);

        // Create Material texture (RGBA16F) - Roughness, Metallic, Emissive, Reflectivity
        materialTex = DynamicTexture2D.Create(width, height, PixelInternalFormat.Rgba16f, debugName: "gMaterial");
        ConfigureAsNonMipRenderTarget(materialTex);

        isInitialized = true;
        capi.Logger.Notification($"[VGE] Created G-buffer textures: {width}x{height}");
        capi.Logger.Notification($"[VGE]   Normal ID={NormalTextureId}, Material ID={MaterialTextureId}");
    }

    private static void ConfigureAsNonMipRenderTarget(DynamicTexture2D texture)
    {
        if (texture is null || !texture.IsValid)
        {
            return;
        }

        // These textures are render targets and are sampled by raw texture ID in some paths.
        // Explicitly disable mipmapping on the texture object (not just via sampler objects).
        texture.DisableMipmaps();
        texture.SetTexFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
        texture.SetTexWrap(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
    }

    private void DeleteTextures()
    {
        normalTex?.Dispose();
        materialTex?.Dispose();
        normalTex = null;
        materialTex = null;
        isInitialized = false;
        isInjected = false;
    }

    /// <summary>
    /// Attaches the G-buffer textures to the specified framebuffer.
    /// </summary>
    /// <param name="fboId">The framebuffer ID to attach to</param>
    private void AttachToFramebuffer(int fboId)
    {
        var gl = GlStateCache.Current;
        using var _ = gl.BindFramebufferScope(FramebufferTarget.Framebuffer, fboId);

        // Attach normal texture as ColorAttachment4 (matches layout(location = 4))
        GL.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment4,
            TextureTarget.Texture2D,
            NormalTextureId,
            0);

        // Attach material texture as ColorAttachment5 (matches layout(location = 5))
        GL.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment5,
            TextureTarget.Texture2D,
            MaterialTextureId,
            0);

        ApplyGBufferBlendState(forceDirty: true);

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
    }

    /// <summary>
    /// Detaches the G-buffer textures from the specified framebuffer.
    /// </summary>
    /// <param name="fboId">The framebuffer ID to detach from</param>
    private void DetachFromFramebuffer(int fboId)
    {
        var gl = GlStateCache.Current;
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, fboId);

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

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        
        capi.Logger.Notification("[VGE] G-buffer detached from Primary framebuffer");
    }
    
    #endregion

    #region IDisposable

    public void Dispose()
    {
        // Clean up textures (framebuffer attachment cleanup happens via UnloadGBuffer hook)
        DeleteTextures();
        
        // Clear the static instance
        if (Instance == this)
        {
            Instance = null;
        }
    }
    
    #endregion
}
