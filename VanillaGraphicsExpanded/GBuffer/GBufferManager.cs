using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded;

/// <summary>
/// Manages G-buffer attachments for the Primary framebuffer through the restricted engine framebuffer integration boundary.
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
    public GpuFramebuffer PrimaryFramebuffer { get; } = DynamicGpuFramebuffer.Wrap(0, "GBuffer.Primary");

    /// <summary>OpenGL name of the shared normal, material and environment array.</summary>
    public int SurfaceTextureId => surfaceTex?.TextureId ?? 0;

    /// <summary>
    /// The OpenGL texture ID for the patch id G-buffer (ColorAttachment6).
    /// Format: RGBA32UI - (chunkSlot, patchId, packedPatchUv, misc/flags).
    /// </summary>
    public int PatchIdTextureId => patchIdTex?.TextureId ?? 0;

    /// <summary>Supplies the current patch identity image without transferring its storage ownership.</summary>
    public GpuTextureRef PatchIdTexture => patchIdTex;


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
        PublishPrimaryInputs(primaryFb);
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
        OpenGLRHIContext.RouteEngineFramebuffer(drawBuffers, GBufferBlendPso, forceDirty: true);
    }

    /// <summary>
    /// Called by Harmony hook after VS sets global blend state via GlToggleBlend.
    /// Reapplies G-buffer blend state that was overwritten by global GL.BlendFunc.
    /// </summary>
    public void ReapplyGBufferBlendState()
    {
        if (!isInitialized || !isInjected)
            return;

        FrameBufferRef? primary = capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        if (primary is not null) OpenGLRHIContext.ReapplyEngineFramebufferBlend(primary.FboId, GBufferBlendPso);
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

        // Clear every layer directly without changing pass routing or write masks.
        surfaceTex?.ClearToZero();
        patchIdTex?.ClearToZero();
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
            PublishPrimaryInputs(primaryFb);
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

    /// <summary>Replaces the owned image set while preserving engine deletion ownership.</summary>
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

    /// <summary>Releases owned images and borrowed recipients through their manager-certified lifetimes.</summary>
    private void DeleteTextures()
    {
        fallbackPosition?.Dispose();
        fallbackPosition = null;
        borrowedPosition?.Dispose();
        borrowedPosition = null;
        PositionTextureId = 0;

        textures?.Dispose();
        textures = null;
        surfaceTex = null;
        patchIdTex = null;
        isInitialized = false;
        isInjected = false;
    }

    /// <summary>
    /// Attaches the G-buffer textures to the specified framebuffer.
    /// </summary>
    /// <param name="fboId">The framebuffer ID to attach to</param>
    private void AttachToFramebuffer(int fboId)
    {
        bool complete = OpenGLRHIContext.ConfigureEngineFramebuffer(fboId,
            new Dictionary<FramebufferAttachment, GpuFramebufferAttachment?>
            {
                [FramebufferAttachment.ColorAttachment3] = GpuFramebufferAttachment.FromTextureId(PositionTextureId),
                [FramebufferAttachment.ColorAttachment4] = GpuFramebufferAttachment.FromTexture(surfaceTex!, layer: GBufferTextures.NormalLayer),
                [FramebufferAttachment.ColorAttachment5] = GpuFramebufferAttachment.FromTexture(surfaceTex!, layer: GBufferTextures.MaterialLayer),
                [FramebufferAttachment.ColorAttachment6] = GpuFramebufferAttachment.FromTexture(patchIdTex!),
                [FramebufferAttachment.ColorAttachment7] = GpuFramebufferAttachment.FromTexture(surfaceTex!, layer: GBufferTextures.EnvironmentLayer)
            });
        OpenGLRHIContext.ApplyEngineFramebufferBlend(GBufferBlendPso, forceDirty: true);
        if (!complete) capi.Logger.Error("[VGE] Framebuffer incomplete after attaching G-buffer.");
    }

    /// <summary>Removes only VGE-owned attachment selections and restores engine routing.</summary>
    private void DetachFromFramebuffer(int fboId)
    {
        OpenGLRHIContext.DetachEngineFramebuffer(fboId,
            [FramebufferAttachment.ColorAttachment4, FramebufferAttachment.ColorAttachment5,
                FramebufferAttachment.ColorAttachment6, FramebufferAttachment.ColorAttachment7],
            [DrawBuffersEnum.ColorAttachment0, DrawBuffersEnum.ColorAttachment1,
                DrawBuffersEnum.ColorAttachment2, DrawBuffersEnum.ColorAttachment3]);
        if (PrimaryFramebuffer.IsValid && PrimaryFramebuffer.FboId == fboId)
            PrimaryFramebuffer.RefreshWrappedFramebuffer(fboId, PrimaryFramebuffer.Width, PrimaryFramebuffer.Height, publishPassMetadata: true);
    }

    #endregion

    #region IDisposable

    /// <summary>Retires the borrowed primary representation and owned G-buffer attachments.</summary>
    public void Dispose()
    {
        unregisterResize();
        RetirePrimaryInputs();
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
