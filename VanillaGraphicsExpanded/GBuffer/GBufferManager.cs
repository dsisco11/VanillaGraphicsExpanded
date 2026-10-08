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
/// - ColorAttachment6: PatchId buffer (RGBA32UI) - layout(location = 6) out uvec4
/// - ColorAttachment7: Local environment irradiance approximation (RGBA16F)
///
/// Integrates with VS via Harmony hooks for framebuffer lifecycle management.
/// </summary>
public sealed partial class GBufferManager : IDisposable
{
    #region Static Instance

    /// <summary>
    /// Singleton instance accessible from Harmony hooks.
    /// </summary>
    public static GBufferManager? Instance { get; private set; }

    #endregion

    #region Fields

    private readonly ICoreClientAPI capi;
    private readonly Action unregisterResize;

    // Owned floating-point array and separate integer patch storage
    private GBufferTextures? textures;
    private Texture3D? surfaceTex;

    private DynamicTexture2D? patchIdTex;

    private const int NormalSlotId = 4;
    private const int MaterialSlotId = 5;
    private const int PatchIdSlotId = 6;
    private const int EnvironmentSlotId = 7;
    private readonly float[] clearColor = [0f, 0f, 0f, 0f];
    private static readonly int[] clearUInt4AsInt = [0, 0, 0, 0];

    private static readonly GlPipelineDesc GBufferBlendPso = new(
        defaultMask: default(GlPipelineStateMask)
            .With(GlPipelineStateId.BlendEnableIndexed)
            .With(GlPipelineStateId.BlendFuncIndexed),
        nonDefaultMask: default,
        blendEnableIndexedAttachments: [(byte)PositionSlotId, (byte)NormalSlotId, (byte)MaterialSlotId, (byte)PatchIdSlotId, (byte)EnvironmentSlotId],
        blendFuncIndexed:
        [
            new GlBlendFuncIndexed((byte)NormalSlotId, GlBlendFunc.Default),
            new GlBlendFuncIndexed((byte)MaterialSlotId, GlBlendFunc.Default),
            new GlBlendFuncIndexed((byte)EnvironmentSlotId, GlBlendFunc.Default)
        ]);

    private int lastWidth;
    private int lastHeight;

    /// <summary>
    /// Whether the G-buffer textures have been created and are ready for attachment.
    /// </summary>
    private bool isInitialized;

    /// <summary>
    /// Whether the G-buffer textures have been attached to the current primary framebuffer.
    /// </summary>
    private bool isInjected;
    private bool primaryRefreshPending = true;

    #endregion

    #region Properties

    /// <summary>Supplies normal, material and environment layers in one owned array.</summary>
    public Texture3D? SurfaceTexture => surfaceTex;

    /// <summary>Persistent borrowed representation of the engine primary framebuffer with injected deferred attachments.</summary>
    public GpuFramebuffer PrimaryFramebuffer { get; } = GpuFramebuffer.Wrap(0, "GBuffer.Primary");

    /// <summary>OpenGL name of the shared normal, material and environment array.</summary>
    public int SurfaceTextureId => surfaceTex?.TextureId ?? 0;

    /// <summary>
    /// The OpenGL texture ID for the patch id G-buffer (ColorAttachment6).
    /// Format: RGBA32UI - (chunkSlot, patchId, packedPatchUv, misc/flags).
    /// </summary>
    public int PatchIdTextureId => patchIdTex?.TextureId ?? 0;


    /// <summary>
    /// Whether the G-buffer textures have been created and are ready for attachment.
    /// </summary>
    public bool IsInitialized => isInitialized;

    #endregion

    #region Constructor / Destructor

    /// <summary>Registers primary attachment setup after engine framebuffer publication and retirement.</summary>
    public GBufferManager(ICoreClientAPI capi)
    {
        this.capi = capi;
        Instance = this;
        unregisterResize = ScreenResourceManager.Register(
            ScreenResourceManager.GBufferOrder,
            OnScreenResized);
    }


    #endregion

    #region Harmony Hook Methods

    /// <summary>
    /// Creates or reattaches primary targets after engine framebuffer replacement or initial primary load.
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
        bool attachmentsRecreated = !isInitialized || width != lastWidth || height != lastHeight;
        if (attachmentsRecreated)
        {
            CreateGBufferTextures(width, height);
            lastWidth = width;
            lastHeight = height;
        }

        // Label VS framebuffer and textures for debugging
        Rendering.Diagnostics.EngineFramebufferDebugLabels.Apply(primaryFb, "VS.Primary");

        // Keep VGE-owned textures out of the engine deletion array. They are attached directly
        // below and remain exclusively owned by this manager across framebuffer rebuilds.
        isInjected = true;

        // Reborrow engine position and attach owned targets even when dimensions are unchanged:
        // an equal-sized rebuild still replaces the primary FBO and deletes its old position texture.
        PrepareReceiverPosition(primaryFb, width, height);
        AttachToFramebuffer(primaryFb.FboId);
        // Normal primary loads can repeat setup without changing any attachment identity.
        if (primaryRefreshPending || attachmentsRecreated || PrimaryFramebuffer.FboId != primaryFb.FboId)
        {
            PrimaryFramebuffer.RefreshWrappedFramebuffer(primaryFb.FboId, width, height, publishPassMetadata: true);
            primaryRefreshPending = false;
        }
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
        // VS sets 0-3; VGE publishes material and environment metadata at 4-7.
        DrawBuffersEnum[] drawBuffers = [
            DrawBuffersEnum.ColorAttachment0,  // VS: outColor (Albedo)
            DrawBuffersEnum.ColorAttachment1,  // VS: outGlow
            DrawBuffersEnum.ColorAttachment2,  // VS: outGNormal (SSAO)
            DrawBuffersEnum.ColorAttachment3,  // VS: outGPosition (SSAO)
            DrawBuffersEnum.ColorAttachment4,  // VGE: Normal
            DrawBuffersEnum.ColorAttachment5,  // VGE: Material
            DrawBuffersEnum.ColorAttachment6,
            DrawBuffersEnum.ColorAttachment7   // VGE: Local environment
        ];
        GL.DrawBuffers(8, drawBuffers);

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
        var gl = StateCache.Current;

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
        int currentFbo = StateCache.Current.GetCurrentFramebuffer(FramebufferTarget.Framebuffer);
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

        string preExistingErrors = GlDebug.GetErrorsString("GBuffer blend verification before queries");
        if (preExistingErrors.Length != 0)
        {
            capi.Logger.Warning($"[VGE] {preExistingErrors}");
        }

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

        if (framebuffer != EnumFrameBuffer.Primary)
        {
            return;
        }

        // Prefer clearing the textures directly. This is robust even if VS unbinds the FBO
        // before our ClearFrameBuffer postfix runs (and also avoids draw-buffer state issues).
        // Fallback to glClearBuffer on the primary FBO if clear-texture isn't available.
        bool clearedSurface = surfaceTex?.TryClearToZero() == true;
        bool clearedPatchId = patchIdTex?.TryClearToZero() == true;

        if (clearedSurface && clearedPatchId)
        {
            return;
        }

        FrameBufferRef? primaryFb = capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        if (primaryFb is null)
        {
            return;
        }

        // Fallback: bind the primary FBO and clear the attachments by index.
        var gl = StateCache.Current;
        using var _ = gl.BindFramebufferScope(FramebufferTarget.Framebuffer, primaryFb.FboId);

        // Ensure our draw buffers are addressable (some drivers validate indices against the active list).
        DrawBuffersEnum[] drawBuffers =
        [
            DrawBuffersEnum.ColorAttachment0,
            DrawBuffersEnum.ColorAttachment1,
            DrawBuffersEnum.ColorAttachment2,
            DrawBuffersEnum.ColorAttachment3,
            DrawBuffersEnum.ColorAttachment4,
            DrawBuffersEnum.ColorAttachment5,
            DrawBuffersEnum.ColorAttachment6,
            DrawBuffersEnum.ColorAttachment7
        ];
        GL.DrawBuffers(8, drawBuffers);

        if (!clearedSurface)
        {
            GL.ClearBuffer(ClearBuffer.Color, NormalSlotId, clearColor);
        }

        if (!clearedSurface)
        {
            GL.ClearBuffer(ClearBuffer.Color, MaterialSlotId, clearColor);
        }

        if (!clearedSurface) GL.ClearBuffer(ClearBuffer.Color, EnvironmentSlotId, clearColor);

        if (!clearedPatchId)
        {
            GL.ClearBuffer(ClearBuffer.Color, PatchIdSlotId, clearUInt4AsInt);
        }
    }

    /// <summary>
    /// Called by Harmony hook when VS unloads a framebuffer.
    /// Used for returning the GL state to default if needed.
    /// </summary>
    /// <param name="framebuffer">The framebuffer being unloaded</param>
    public void UnloadGBuffer(EnumFrameBuffer framebuffer)
    {
        if (framebuffer != EnumFrameBuffer.Primary || !isInjected)
        {
            return;
        }

        isInjected = false;
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
            PrepareReceiverPosition(primaryFb, screenWidth, screenHeight);
            AttachToFramebuffer(primaryFb.FboId);
            PrimaryFramebuffer.RefreshWrappedFramebuffer(primaryFb.FboId, screenWidth, screenHeight, publishPassMetadata: true);

            isInjected = true;
            capi.Logger.Debug($"[VGE] EnsureBuffers: Recreated G-buffer textures for {screenWidth}x{screenHeight}");
        }

        // Return true only if we have valid texture IDs
        return isInitialized && SurfaceTextureId != 0 && PatchIdTextureId != 0;
    }

    #endregion

    #region Private Methods

    /// <summary>Publishes a completed engine rebuild even when dimensions and GL names are reused.</summary>
    private void OnScreenResized()
    {
        primaryRefreshPending = true;
        SetupGBuffers();
    }

    private void CreateGBufferTextures(int width, int height)
    {
        // Delete old textures if they exist
        DeleteTextures();

        textures = new(width, height);
        surfaceTex = textures.Surface; patchIdTex = textures.PatchId;

        isInitialized = true;
        capi.Logger.Notification($"[VGE] Created G-buffer textures: {width}x{height}");
        capi.Logger.Notification($"[VGE]   Surface array ID={SurfaceTextureId}, PatchId ID={PatchIdTextureId}");
    }

    private void DeleteTextures()
    {
        fallbackPosition?.Dispose();
        fallbackPosition = null;
        PositionTextureId = 0;

        bool externallyDeleted = RelinquishIfDeleted(surfaceTex) | RelinquishIfDeleted(patchIdTex);
        if (externallyDeleted)
        {
            StateCache.Current.InvalidateAll();
        }

        textures?.Dispose();
        textures = null;
        surfaceTex = null;
        patchIdTex = null;
        isInitialized = false;
        isInjected = false;
    }

    private static bool RelinquishIfDeleted(GpuTexture? texture)
    {
        if (texture is null || !texture.IsValid || GL.IsTexture(texture.TextureId))
        {
            return false;
        }

        texture.ReleaseHandle();
        return true;
    }

    /// <summary>
    /// Attaches the G-buffer textures to the specified framebuffer.
    /// </summary>
    /// <param name="fboId">The framebuffer ID to attach to</param>
    private void AttachToFramebuffer(int fboId)
    {
        var gl = StateCache.Current;
        using var _ = gl.BindFramebufferScope(FramebufferTarget.Framebuffer, fboId);

        // Reuse the engine SSAO position target, or occupy its vacant slot when SSAO is disabled.
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment3,
            TextureTarget.Texture2D, PositionTextureId, 0);

        // Attach normal texture as ColorAttachment4 (matches layout(location = 4))
        GL.FramebufferTextureLayer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment4, surfaceTex!.TextureId, 0, GBufferTextures.NormalLayer);

        // Attach material texture as ColorAttachment5 (matches layout(location = 5))
        GL.FramebufferTextureLayer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment5, surfaceTex!.TextureId, 0, GBufferTextures.MaterialLayer);

        GL.FramebufferTextureLayer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment7, surfaceTex!.TextureId, 0, GBufferTextures.EnvironmentLayer);

        // Attach patch id texture as ColorAttachment6 (matches layout(location = 6))
        GL.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment6,
            TextureTarget.Texture2D,
            PatchIdTextureId,
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
            capi.Logger.Notification("[VGE] G-buffer textures attached to Primary framebuffer (Normal@4, Material@5, PatchId@6, Environment@7)");
        }
    }

    /// <summary>
    /// Detaches the G-buffer textures from the specified framebuffer.
    /// </summary>
    /// <param name="fboId">The framebuffer ID to detach from</param>
    private void DetachFromFramebuffer(int fboId)
    {
        var gl = StateCache.Current;
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, fboId);

        // Detach our color textures (ColorAttachment4-7)
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

        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment7,
            TextureTarget.Texture2D, 0, 0);

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
        if (PrimaryFramebuffer.IsValid && PrimaryFramebuffer.FboId == fboId)
        {
            PrimaryFramebuffer.RefreshWrappedFramebuffer(fboId, PrimaryFramebuffer.Width, PrimaryFramebuffer.Height, publishPassMetadata: true);
        }
    }

    #endregion

    #region IDisposable

    /// <summary>Retires the borrowed primary representation and owned G-buffer attachments.</summary>
    public void Dispose()
    {
        unregisterResize();
        PrimaryFramebuffer.Dispose();
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
