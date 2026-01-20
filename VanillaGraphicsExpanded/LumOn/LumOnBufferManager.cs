using System;
using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Manages GPU textures for LumOn probe grid and radiance cache.
/// Creates and maintains framebuffers for:
/// - Probe anchor positions and normals
/// - Radiance cache (SH coefficients) with double-buffering for temporal
/// - Indirect diffuse output at half and full resolution
/// </summary>
public sealed class LumOnBufferManager : IDisposable
{
    #region Fields

    private readonly ICoreClientAPI capi;
    private readonly LumOnConfig config;

    // Screen dimensions tracking
    private int lastScreenWidth;
    private int lastScreenHeight;

    // Probe grid dimensions (computed from screen size and spacing)
    private int probeCountX;
    private int probeCountY;

    // Half-resolution dimensions
    private int halfResWidth;
    private int halfResHeight;

    // ═══════════════════════════════════════════════════════════════
    // Probe Anchor Buffers
    // ═══════════════════════════════════════════════════════════════

    private DynamicTexture2D? probeAnchorPositionTex;
    private DynamicTexture2D? probeAnchorNormalTex;
    private Rendering.GBuffer? probeAnchorFbo;

    // ═══════════════════════════════════════════════════════════════
    // Radiance Cache Buffers (Triple-buffered for temporal)
    // ═══════════════════════════════════════════════════════════════

    // Trace output radiance (written by trace pass, read by temporal pass)
    private DynamicTexture2D? radianceTraceTex0;
    private DynamicTexture2D? radianceTraceTex1;
    private Rendering.GBuffer? radianceTraceFbo;

    // Current frame radiance (written by temporal pass, read by gather)
    private DynamicTexture2D? radianceCurrentTex0;
    private DynamicTexture2D? radianceCurrentTex1;
    private Rendering.GBuffer? radianceCurrentFbo;

    // History radiance (previous frame's current, read by temporal pass)
    private DynamicTexture2D? radianceHistoryTex0;
    private DynamicTexture2D? radianceHistoryTex1;
    private Rendering.GBuffer? radianceHistoryFbo;

    // ═══════════════════════════════════════════════════════════════
    // Screen-Probe Atlas (2D atlas layout)
    // Implementation detail: octahedral direction mapping per probe tile.
    // Layout: (probeCountX * 8, probeCountY * 8) - tiled 8×8 per probe
    // RGBA16F: RGB = radiance, A = log-encoded hit distance
    // ═══════════════════════════════════════════════════════════════

    // Trace output probe atlas (written by trace pass, read by temporal pass)
    private DynamicTexture2D? screenProbeAtlasTraceTex;
    private Rendering.GBuffer? screenProbeAtlasTraceFbo;

    // Trace output meta (written by trace pass, read by temporal pass)
    // RG32F: R = confidence, G = uintBitsToFloat(flags)
    private DynamicTexture2D? screenProbeAtlasMetaTraceTex;

    // Current frame probe atlas (written by temporal pass, read by gather)
    private DynamicTexture2D? screenProbeAtlasCurrentTex;
    private Rendering.GBuffer? screenProbeAtlasCurrentFbo;

    // Current frame meta (written by temporal pass, swapped to history each frame)
    private DynamicTexture2D? screenProbeAtlasMetaCurrentTex;

    // History probe atlas (previous frame's current, read by temporal pass)
    private DynamicTexture2D? screenProbeAtlasHistoryTex;
    private Rendering.GBuffer? screenProbeAtlasHistoryFbo;

    // History meta (previous frame's current, read by trace/temporal passes)
    private DynamicTexture2D? screenProbeAtlasMetaHistoryTex;

    // Filtered probe atlas (derived from current frame's temporal output)
    // Used as gather input when available.
    private DynamicTexture2D? screenProbeAtlasFilteredTex;
    private DynamicTexture2D? screenProbeAtlasMetaFilteredTex;
    private Rendering.GBuffer? screenProbeAtlasFilteredFbo;

    // ═══════════════════════════════════════════════════════════════
    // Probe-Atlas → SH9 Projection Output (Option B)
    // Stores packed SH9 coefficients per probe across 7 RGBA16F textures.
    // Used by the cheap SH9 gather path.
    // ═══════════════════════════════════════════════════════════════

    private DynamicTexture2D? probeSh9Tex0;
    private DynamicTexture2D? probeSh9Tex1;
    private DynamicTexture2D? probeSh9Tex2;
    private DynamicTexture2D? probeSh9Tex3;
    private DynamicTexture2D? probeSh9Tex4;
    private DynamicTexture2D? probeSh9Tex5;
    private DynamicTexture2D? probeSh9Tex6;
    private Rendering.GBuffer? probeSh9Fbo;

    // ═══════════════════════════════════════════════════════════════
    // Probe Metadata Buffers (for temporal validation)
    // ═══════════════════════════════════════════════════════════════

    private DynamicTexture2D? probeMetaCurrentTex;
    private Rendering.GBuffer? probeMetaCurrentFbo;

    // History metadata (swapped each frame)
    private DynamicTexture2D? probeMetaHistoryTex;
    private Rendering.GBuffer? probeMetaHistoryFbo;

    // Temporal output FBO (MRT: radiance0, radiance1, meta to current buffers)
    private Rendering.GBuffer? temporalOutputFbo;

    // ═══════════════════════════════════════════════════════════════
    // Indirect Diffuse Output Buffers
    // ═══════════════════════════════════════════════════════════════

    private DynamicTexture2D? indirectHalfTex;
    private Rendering.GBuffer? indirectHalfFbo;

    private DynamicTexture2D? indirectFullTex;
    private Rendering.GBuffer? indirectFullFbo;

    // ═══════════════════════════════════════════════════════════════
    // Captured Scene Buffer (for radiance sampling)
    // ═══════════════════════════════════════════════════════════════

    private DynamicTexture2D? capturedSceneTex;
    private Rendering.GBuffer? capturedSceneFbo;

    // ═══════════════════════════════════════════════════════════════
    // Reprojection Velocity Buffer (Phase 14)
    // RGBA32F: RG = velocityUv, A = uintBitsToFloat(flags)
    // ═══════════════════════════════════════════════════════════════

    private DynamicTexture2D? velocityTex;
    private Rendering.GBuffer? velocityFbo;

    // ═══════════════════════════════════════════════════════════════
    // Depth Pyramid / HZB
    // ═══════════════════════════════════════════════════════════════

    private DynamicTexture2D? hzbDepthTex;
    private int hzbFboId;

    // Double-buffer swap index (0 or 1)
    private int currentBufferIndex;

    private bool isInitialized;

    private bool forceRecreateOnNextEnsure;

    #endregion

    #region Properties

    /// <summary>
    /// Number of probes horizontally in the grid.
    /// </summary>
    public int ProbeCountX => probeCountX;

    /// <summary>
    /// Number of probes vertically in the grid.
    /// </summary>
    public int ProbeCountY => probeCountY;

    // ═══════════════════════════════════════════════════════════════
    // Probe Anchor Buffers
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// FBO for probe anchor pass output (position + normal).
    /// </summary>
    public Rendering.GBuffer? ProbeAnchorFbo => probeAnchorFbo;

    /// <summary>
    /// Texture for probe anchor positions (posVS.xyz, valid).
    /// </summary>
    public DynamicTexture2D? ProbeAnchorPositionTex => probeAnchorPositionTex;

    /// <summary>
    /// Texture for probe anchor normals (normalVS.xyz, reserved).
    /// </summary>
    public DynamicTexture2D? ProbeAnchorNormalTex => probeAnchorNormalTex;

    // ═══════════════════════════════════════════════════════════════
    // Radiance Cache Buffers
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// FBO for trace pass radiance output.
    /// </summary>
    public Rendering.GBuffer? RadianceTraceFbo => radianceTraceFbo;

    /// <summary>
    /// Texture for trace radiance SH coefficients (set 0).
    /// </summary>
    public DynamicTexture2D? RadianceTraceTex0 => radianceTraceTex0;

    /// <summary>
    /// Texture for trace radiance SH coefficients (set 1).
    /// </summary>
    public DynamicTexture2D? RadianceTraceTex1 => radianceTraceTex1;

    /// <summary>
    /// FBO for current frame radiance output.
    /// </summary>
    public Rendering.GBuffer? RadianceCurrentFbo => radianceCurrentFbo;

    /// <summary>
    /// Texture for current radiance SH coefficients (set 0).
    /// </summary>
    public DynamicTexture2D? RadianceCurrentTex0 => radianceCurrentTex0;

    /// <summary>
    /// Texture for current radiance SH coefficients (set 1).
    /// </summary>
    public DynamicTexture2D? RadianceCurrentTex1 => radianceCurrentTex1;

    /// <summary>
    /// FBO for history radiance (read during temporal blend).
    /// </summary>
    public Rendering.GBuffer? RadianceHistoryFbo => radianceHistoryFbo;

    /// <summary>
    /// Texture for history radiance SH coefficients (set 0).
    /// </summary>
    public DynamicTexture2D? RadianceHistoryTex0 => radianceHistoryTex0;

    /// <summary>
    /// Texture for history radiance SH coefficients (set 1).
    /// </summary>
    public DynamicTexture2D? RadianceHistoryTex1 => radianceHistoryTex1;

    // ═══════════════════════════════════════════════════════════════
    // Screen-Probe Atlas (2D atlas)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 2D atlas for trace output probe-atlas radiance.
    /// Layout: (probeCountX * 8, probeCountY * 8), RGBA16F.
    /// </summary>
    public DynamicTexture2D? ScreenProbeAtlasTraceTex => screenProbeAtlasTraceTex;

    /// <summary>
    /// 2D atlas for trace output probe-atlas meta.
    /// Format: RG32F (confidence, flagsBitsAsFloat).
    /// </summary>
    public DynamicTexture2D? ScreenProbeAtlasMetaTraceTex => screenProbeAtlasMetaTraceTex;

    /// <summary>
    /// FBO for probe-atlas trace output.
    /// </summary>
    public Rendering.GBuffer? ScreenProbeAtlasTraceFbo => screenProbeAtlasTraceFbo;

    /// <summary>
    /// 2D atlas for current frame probe-atlas radiance (after temporal blend).
    /// </summary>
    public DynamicTexture2D? ScreenProbeAtlasCurrentTex => screenProbeAtlasCurrentTex;

    /// <summary>
    /// 2D atlas for current frame probe-atlas meta (after temporal pass).
    /// </summary>
    public DynamicTexture2D? ScreenProbeAtlasMetaCurrentTex => screenProbeAtlasMetaCurrentTex;

    /// <summary>
    /// 2D atlas for filtered probe-atlas radiance (post-temporal denoise).
    /// </summary>
    public DynamicTexture2D? ScreenProbeAtlasFilteredTex => screenProbeAtlasFilteredTex;

    /// <summary>
    /// 2D atlas for filtered probe-atlas meta (post-temporal denoise).
    /// </summary>
    public DynamicTexture2D? ScreenProbeAtlasMetaFilteredTex => screenProbeAtlasMetaFilteredTex;

    /// <summary>
    /// FBO for probe-atlas filtered output.
    /// </summary>
    public Rendering.GBuffer? ScreenProbeAtlasFilteredFbo => screenProbeAtlasFilteredFbo;

    /// <summary>
    /// FBO for SH9 projection output (7 MRT attachments).
    /// </summary>
    public Rendering.GBuffer? ProbeSh9Fbo => probeSh9Fbo;

    public DynamicTexture2D? ProbeSh9Tex0 => probeSh9Tex0;
    public DynamicTexture2D? ProbeSh9Tex1 => probeSh9Tex1;
    public DynamicTexture2D? ProbeSh9Tex2 => probeSh9Tex2;
    public DynamicTexture2D? ProbeSh9Tex3 => probeSh9Tex3;
    public DynamicTexture2D? ProbeSh9Tex4 => probeSh9Tex4;
    public DynamicTexture2D? ProbeSh9Tex5 => probeSh9Tex5;
    public DynamicTexture2D? ProbeSh9Tex6 => probeSh9Tex6;

    /// <summary>
    /// FBO for probe-atlas current output.
    /// </summary>
    public Rendering.GBuffer? ScreenProbeAtlasCurrentFbo => screenProbeAtlasCurrentFbo;

    /// <summary>
    /// 2D atlas for history probe-atlas radiance (previous frame).
    /// </summary>
    public DynamicTexture2D? ScreenProbeAtlasHistoryTex => screenProbeAtlasHistoryTex;

    /// <summary>
    /// 2D atlas for history probe-atlas meta (previous frame).
    /// </summary>
    public DynamicTexture2D? ScreenProbeAtlasMetaHistoryTex => screenProbeAtlasMetaHistoryTex;

    /// <summary>
    /// Width of the probe atlas (probeCountX × 8).
    /// </summary>
    public int ScreenProbeAtlasWidth => probeCountX * 8;

    /// <summary>
    /// Height of the probe atlas (probeCountY × 8).
    /// </summary>
    public int ScreenProbeAtlasHeight => probeCountY * 8;

    /// <summary>
    /// Total number of probes (probeCountX × probeCountY).
    /// </summary>
    public int ProbeCount => probeCountX * probeCountY;

    // ═══════════════════════════════════════════════════════════════
    // Probe Metadata Buffers
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// FBO for current frame probe metadata.
    /// </summary>
    public Rendering.GBuffer? ProbeMetaCurrentFbo => probeMetaCurrentFbo;

    /// <summary>
    /// Texture for current frame probe metadata (depth, normal, accumCount).
    /// </summary>
    public DynamicTexture2D? ProbeMetaCurrentTex => probeMetaCurrentTex;

    /// <summary>
    /// FBO for history probe metadata.
    /// </summary>
    public Rendering.GBuffer? ProbeMetaHistoryFbo => probeMetaHistoryFbo;

    /// <summary>
    /// Texture for history probe metadata.
    /// </summary>
    public DynamicTexture2D? ProbeMetaHistoryTex => probeMetaHistoryTex;

    /// <summary>
    /// FBO for temporal pass MRT output (radiance0, radiance1, meta).
    /// </summary>
    public Rendering.GBuffer? TemporalOutputFbo => temporalOutputFbo;

    // ═══════════════════════════════════════════════════════════════
    // Indirect Diffuse Output Buffers
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// FBO for half-resolution indirect diffuse output.
    /// </summary>
    public Rendering.GBuffer? IndirectHalfFbo => indirectHalfFbo;

    /// <summary>
    /// Texture for half-resolution indirect diffuse.
    /// </summary>
    public DynamicTexture2D? IndirectHalfTex => indirectHalfTex;

    /// <summary>
    /// FBO for full-resolution indirect diffuse output.
    /// </summary>
    public Rendering.GBuffer? IndirectFullFbo => indirectFullFbo;

    /// <summary>
    /// Texture for full-resolution indirect diffuse (final output).
    /// </summary>
    public DynamicTexture2D? IndirectFullTex => indirectFullTex;

    // ═══════════════════════════════════════════════════════════════
    // Captured Scene Buffer
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// FBO for captured scene (used for blitting).
    /// </summary>
    public Rendering.GBuffer? CapturedSceneFbo => capturedSceneFbo;

    /// <summary>
    /// Texture for captured scene (radiance sampling source).
    /// </summary>
    public DynamicTexture2D? CapturedSceneTex => capturedSceneTex;

    /// <summary>
    /// FBO for velocity output (full resolution).
    /// </summary>
    public Rendering.GBuffer? VelocityFbo => velocityFbo;

    /// <summary>
    /// Velocity texture (RGBA32F): RG = velocityUv, A = packed flags.
    /// </summary>
    public DynamicTexture2D? VelocityTex => velocityTex;

    /// <summary>
    /// HZB depth pyramid texture (mipmapped R32F), mip 0 matches screen size.
    /// </summary>
    public DynamicTexture2D? HzbDepthTex => hzbDepthTex;

    /// <summary>
    /// FBO id used for rendering into HZB mip levels.
    /// </summary>
    public int HzbFboId => hzbFboId;

    // ═══════════════════════════════════════════════════════════════
    // Dimensions
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Half-resolution buffer width.
    /// </summary>
    public int HalfResWidth => halfResWidth;

    /// <summary>
    /// Half-resolution buffer height.
    /// </summary>
    public int HalfResHeight => halfResHeight;

    /// <summary>
    /// Whether buffers have been initialized.
    /// </summary>
    public bool IsInitialized => isInitialized;

    #endregion

    #region Constructor

    public LumOnBufferManager(ICoreClientAPI capi, LumOnConfig config)
    {
        this.capi = capi;
        this.config = config;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Ensures LumOn buffers are allocated and sized correctly for the current screen dimensions.
    /// Call this each frame before rendering.
    /// </summary>
    /// <param name="screenWidth">Full-resolution screen width</param>
    /// <param name="screenHeight">Full-resolution screen height</param>
    /// <returns>True if buffers are valid and ready, false if not initialized</returns>
    public bool EnsureBuffers(int screenWidth, int screenHeight)
    {
        if (forceRecreateOnNextEnsure || !isInitialized || screenWidth != lastScreenWidth || screenHeight != lastScreenHeight)
        {
            CreateBuffers(screenWidth, screenHeight);
            lastScreenWidth = screenWidth;
            lastScreenHeight = screenHeight;
            forceRecreateOnNextEnsure = false;
            return false;  // Buffers were recreated
        }
        return true;  // No change
    }

    public void RequestRecreateBuffers(string reason)
    {
        forceRecreateOnNextEnsure = true;
        capi.Logger.Debug("[LumOn] Buffer recreation requested: {0}", reason);
    }

    /// <summary>
    /// Swap current/history radiance buffers for temporal accumulation.
    /// Called after temporal pass completes.
    /// </summary>
    public void SwapRadianceBuffers()
    {
        // Swap SH radiance textures (legacy, to be removed in Phase 3+)
        (radianceCurrentTex0, radianceHistoryTex0) = (radianceHistoryTex0, radianceCurrentTex0);
        (radianceCurrentTex1, radianceHistoryTex1) = (radianceHistoryTex1, radianceCurrentTex1);
        (radianceCurrentFbo, radianceHistoryFbo) = (radianceHistoryFbo, radianceCurrentFbo);

        // Swap screen-probe atlas textures (2D atlas)
        (screenProbeAtlasCurrentTex, screenProbeAtlasHistoryTex) = (screenProbeAtlasHistoryTex, screenProbeAtlasCurrentTex);
        (screenProbeAtlasMetaCurrentTex, screenProbeAtlasMetaHistoryTex) = (screenProbeAtlasMetaHistoryTex, screenProbeAtlasMetaCurrentTex);
        (screenProbeAtlasCurrentFbo, screenProbeAtlasHistoryFbo) = (screenProbeAtlasHistoryFbo, screenProbeAtlasCurrentFbo);

        // Swap metadata
        (probeMetaCurrentTex, probeMetaHistoryTex) = (probeMetaHistoryTex, probeMetaCurrentTex);
        (probeMetaCurrentFbo, probeMetaHistoryFbo) = (probeMetaHistoryFbo, probeMetaCurrentFbo);

        currentBufferIndex = 1 - currentBufferIndex;

        // Recreate temporal output FBO to point to the new "current" targets
        CreateTemporalOutputFbo();
    }

    /// <summary>
    /// Clears the radiance history buffer to black.
    /// Call this on first frame, after teleportation, or when history is invalidated.
    /// Forces full recomputation of radiance cache.
    /// </summary>
    public void ClearHistory()
    {
        if (!isInitialized)
            return;

        // Save current framebuffer binding
        int previousFbo = Rendering.GBuffer.SaveBinding();

        // Clear all SH radiance and metadata buffers to black
        radianceHistoryFbo?.BindAndClear();
        radianceCurrentFbo?.BindAndClear();
        probeMetaHistoryFbo?.BindAndClear();
        probeMetaCurrentFbo?.BindAndClear();

        // Clear screen-probe atlas textures (2D atlas)
        screenProbeAtlasTraceFbo?.BindAndClear();
        screenProbeAtlasCurrentFbo?.BindAndClear();
        screenProbeAtlasHistoryFbo?.BindAndClear();
        screenProbeAtlasFilteredFbo?.BindAndClear();
        probeSh9Fbo?.BindAndClear();

        // Clear velocity output (debug/temporal safety on resets)
        velocityFbo?.BindAndClear();

        // Restore previous framebuffer
        Rendering.GBuffer.RestoreBinding(previousFbo);

        capi.Logger.Debug("[LumOn] Cleared radiance history and metadata buffers");
    }

    /// <summary>
    /// Invalidates the radiance cache due to camera discontinuity.
    /// Call when camera teleports or view changes significantly.
    /// </summary>
    /// <param name="reason">Reason for invalidation (for logging)</param>
    public void InvalidateCache(string reason)
    {
        ClearHistory();
        capi.Logger.Notification($"[LumOn] Cache invalidated: {reason}");
    }

    /// <summary>
    /// Captures the current primary framebuffer to the captured scene texture.
    /// Call this before probe tracing to have the lit scene available for radiance sampling.
    /// </summary>
    /// <param name="primaryFboId">The primary framebuffer ID to blit from</param>
    /// <param name="screenWidth">Screen width</param>
    /// <param name="screenHeight">Screen height</param>
    public void CaptureScene(int primaryFboId, int screenWidth, int screenHeight)
    {
        if (!isInitialized || capturedSceneFbo == null)
            return;

        // Blit from primary FB to captured scene texture
        capturedSceneFbo.BlitFromExternal(primaryFboId, screenWidth, screenHeight);
    }

    #endregion

    #region Private Methods

    private void CreateBuffers(int screenWidth, int screenHeight)
    {
        // Delete existing buffers
        DeleteBuffers();

        var cfg = config.LumOn;

        // Calculate probe grid dimensions
        probeCountX = (int)Math.Ceiling((float)screenWidth / cfg.ProbeSpacingPx);
        probeCountY = (int)Math.Ceiling((float)screenHeight / cfg.ProbeSpacingPx);

        // Calculate half-res dimensions
        halfResWidth = cfg.HalfResolution ? screenWidth / 2 : screenWidth;
        halfResHeight = cfg.HalfResolution ? screenHeight / 2 : screenHeight;

        // ═══════════════════════════════════════════════════════════════
        // Create Probe Anchor Buffers
        // ═══════════════════════════════════════════════════════════════

        probeAnchorPositionTex = DynamicTexture2D.Create(probeCountX, probeCountY, PixelInternalFormat.Rgba16f, debugName: "ProbeAnchorPosition");
        probeAnchorNormalTex = DynamicTexture2D.Create(probeCountX, probeCountY, PixelInternalFormat.Rgba16f, debugName: "ProbeAnchorNormal");
        probeAnchorFbo = Rendering.GBuffer.CreateMRT("ProbeAnchorFBO", probeAnchorPositionTex, probeAnchorNormalTex);

        // ═══════════════════════════════════════════════════════════════
        // Create Radiance Cache Buffers (Trace output)
        // ═══════════════════════════════════════════════════════════════

        radianceTraceTex0 = DynamicTexture2D.Create(probeCountX, probeCountY, PixelInternalFormat.Rgba16f, debugName: "RadianceTrace0");
        radianceTraceTex1 = DynamicTexture2D.Create(probeCountX, probeCountY, PixelInternalFormat.Rgba16f, debugName: "RadianceTrace1");
        radianceTraceFbo = Rendering.GBuffer.CreateMRT("RadianceTraceFBO", radianceTraceTex0, radianceTraceTex1);

        // ═══════════════════════════════════════════════════════════════
        // Create Radiance Cache Buffers (Current - temporal output)
        // ═══════════════════════════════════════════════════════════════

        radianceCurrentTex0 = DynamicTexture2D.Create(probeCountX, probeCountY, PixelInternalFormat.Rgba16f, debugName: "RadianceCurrent0");
        radianceCurrentTex1 = DynamicTexture2D.Create(probeCountX, probeCountY, PixelInternalFormat.Rgba16f, debugName: "RadianceCurrent1");
        radianceCurrentFbo = Rendering.GBuffer.CreateMRT("RadianceCurrentFBO", radianceCurrentTex0, radianceCurrentTex1);

        // ═══════════════════════════════════════════════════════════════
        // Create Radiance Cache Buffers (History)
        // ═══════════════════════════════════════════════════════════════

        radianceHistoryTex0 = DynamicTexture2D.Create(probeCountX, probeCountY, PixelInternalFormat.Rgba16f, debugName: "RadianceHistory0");
        radianceHistoryTex1 = DynamicTexture2D.Create(probeCountX, probeCountY, PixelInternalFormat.Rgba16f, debugName: "RadianceHistory1");
        radianceHistoryFbo = Rendering.GBuffer.CreateMRT("RadianceHistoryFBO", radianceHistoryTex0, radianceHistoryTex1);

        // ═══════════════════════════════════════════════════════════════
        // Create Screen-Probe Atlas (2D atlas)
        // Implementation detail: octahedral direction mapping per probe tile.
        // Layout: (probeCountX * 8, probeCountY * 8) - tiled 8×8 per probe
        // RGBA16F: RGB = radiance, A = log-encoded hit distance
        // ═══════════════════════════════════════════════════════════════

        int atlasWidth = probeCountX * 8;
        int atlasHeight = probeCountY * 8;
        screenProbeAtlasTraceTex = DynamicTexture2D.Create(atlasWidth, atlasHeight, PixelInternalFormat.Rgba16f, debugName: "ScreenProbeAtlasTrace");
        screenProbeAtlasMetaTraceTex = DynamicTexture2D.Create(atlasWidth, atlasHeight, PixelInternalFormat.Rg32f, debugName: "ScreenProbeAtlasMetaTrace");
        screenProbeAtlasTraceFbo = Rendering.GBuffer.CreateMRT([screenProbeAtlasTraceTex, screenProbeAtlasMetaTraceTex], null, ownsTextures: false, debugName: "ScreenProbeAtlasTraceFBO");

        screenProbeAtlasCurrentTex = DynamicTexture2D.Create(atlasWidth, atlasHeight, PixelInternalFormat.Rgba16f, debugName: "ScreenProbeAtlasCurrent");
        screenProbeAtlasMetaCurrentTex = DynamicTexture2D.Create(atlasWidth, atlasHeight, PixelInternalFormat.Rg32f, debugName: "ScreenProbeAtlasMetaCurrent");
        screenProbeAtlasCurrentFbo = Rendering.GBuffer.CreateMRT([screenProbeAtlasCurrentTex, screenProbeAtlasMetaCurrentTex], null, ownsTextures: false, debugName: "ScreenProbeAtlasCurrentFBO");

        screenProbeAtlasHistoryTex = DynamicTexture2D.Create(atlasWidth, atlasHeight, PixelInternalFormat.Rgba16f, debugName: "ScreenProbeAtlasHistory");
        screenProbeAtlasMetaHistoryTex = DynamicTexture2D.Create(atlasWidth, atlasHeight, PixelInternalFormat.Rg32f, debugName: "ScreenProbeAtlasMetaHistory");
        screenProbeAtlasHistoryFbo = Rendering.GBuffer.CreateMRT([screenProbeAtlasHistoryTex, screenProbeAtlasMetaHistoryTex], null, ownsTextures: false, debugName: "ScreenProbeAtlasHistoryFBO");

        // Filtered atlas output (Pass 3.5): derived from temporal output each frame
        screenProbeAtlasFilteredTex = DynamicTexture2D.Create(atlasWidth, atlasHeight, PixelInternalFormat.Rgba16f, debugName: "ScreenProbeAtlasFiltered");
        screenProbeAtlasMetaFilteredTex = DynamicTexture2D.Create(atlasWidth, atlasHeight, PixelInternalFormat.Rg32f, debugName: "ScreenProbeAtlasMetaFiltered");
        screenProbeAtlasFilteredFbo = Rendering.GBuffer.CreateMRT([screenProbeAtlasFilteredTex, screenProbeAtlasMetaFilteredTex], null, ownsTextures: false, debugName: "ScreenProbeAtlasFilteredFBO");

        // Probe-atlas → SH9 projection output (Option B)
        // 7 RGBA16F attachments to pack 27 floats (9 RGB coeffs)
        probeSh9Tex0 = DynamicTexture2D.Create(probeCountX, probeCountY, PixelInternalFormat.Rgba16f, debugName: "ProbeSH9_0");
        probeSh9Tex1 = DynamicTexture2D.Create(probeCountX, probeCountY, PixelInternalFormat.Rgba16f, debugName: "ProbeSH9_1");
        probeSh9Tex2 = DynamicTexture2D.Create(probeCountX, probeCountY, PixelInternalFormat.Rgba16f, debugName: "ProbeSH9_2");
        probeSh9Tex3 = DynamicTexture2D.Create(probeCountX, probeCountY, PixelInternalFormat.Rgba16f, debugName: "ProbeSH9_3");
        probeSh9Tex4 = DynamicTexture2D.Create(probeCountX, probeCountY, PixelInternalFormat.Rgba16f, debugName: "ProbeSH9_4");
        probeSh9Tex5 = DynamicTexture2D.Create(probeCountX, probeCountY, PixelInternalFormat.Rgba16f, debugName: "ProbeSH9_5");
        probeSh9Tex6 = DynamicTexture2D.Create(probeCountX, probeCountY, PixelInternalFormat.Rgba16f, debugName: "ProbeSH9_6");
        probeSh9Fbo = Rendering.GBuffer.CreateMRT(
            [probeSh9Tex0, probeSh9Tex1, probeSh9Tex2, probeSh9Tex3, probeSh9Tex4, probeSh9Tex5, probeSh9Tex6],
            null,
            ownsTextures: false,
            debugName: "ProbeSH9FBO");

        // ═══════════════════════════════════════════════════════════════
        // Create Probe Metadata Buffers (for temporal validation)
        // ═══════════════════════════════════════════════════════════════

        probeMetaCurrentTex = DynamicTexture2D.Create(probeCountX, probeCountY, PixelInternalFormat.Rgba16f, debugName: "ProbeMetaCurrent");
        probeMetaCurrentFbo = Rendering.GBuffer.CreateSingle(probeMetaCurrentTex, debugName: "ProbeMetaCurrentFBO");

        probeMetaHistoryTex = DynamicTexture2D.Create(probeCountX, probeCountY, PixelInternalFormat.Rgba16f, debugName: "ProbeMetaHistory");
        probeMetaHistoryFbo = Rendering.GBuffer.CreateSingle(probeMetaHistoryTex, debugName: "ProbeMetaHistoryFBO");

        // Create temporal output FBO (MRT: writes to current radiance + current meta)
        CreateTemporalOutputFbo();

        // ═══════════════════════════════════════════════════════════════
        // Create Indirect Diffuse Output Buffers
        // ═══════════════════════════════════════════════════════════════

        indirectHalfTex = DynamicTexture2D.Create(halfResWidth, halfResHeight, PixelInternalFormat.Rgba16f, debugName: "IndirectHalf");
        indirectHalfFbo = Rendering.GBuffer.CreateSingle(indirectHalfTex, debugName: "IndirectHalfFBO");

        indirectFullTex = DynamicTexture2D.Create(screenWidth, screenHeight, PixelInternalFormat.Rgba16f, debugName: "IndirectFull");
        indirectFullFbo = Rendering.GBuffer.CreateSingle(indirectFullTex, debugName: "IndirectFullFBO");

        // ═══════════════════════════════════════════════════════════════
        // Create Captured Scene Buffer
        // ═══════════════════════════════════════════════════════════════

        capturedSceneTex = DynamicTexture2D.Create(screenWidth, screenHeight, PixelInternalFormat.Rgba16f, TextureFilterMode.Linear, debugName: "CapturedScene");
        capturedSceneFbo = Rendering.GBuffer.CreateSingle(capturedSceneTex, debugName: "CapturedSceneFBO");

        // ═══════════════════════════════════════════════════════════════
        // Velocity Buffer (Phase 14)
        // NOTE: Packed uintBitsToFloat flags require a 32-bit float channel.
        // We use RGBA32F for simplicity and correctness.
        // ═══════════════════════════════════════════════════════════════

        velocityTex = DynamicTexture2D.Create(screenWidth, screenHeight, PixelInternalFormat.Rgba32f, TextureFilterMode.Nearest, debugName: "Velocity");
        velocityFbo = Rendering.GBuffer.CreateSingle(velocityTex, debugName: "VelocityFBO");

        // ═══════════════════════════════════════════════════════════════
        // HZB Depth Pyramid (mipmapped R32F)
        // ═══════════════════════════════════════════════════════════════

        int maxDim = Math.Max(screenWidth, screenHeight);
        int mipLevels = 1;
        while ((maxDim >>= 1) > 0) mipLevels++;

        hzbDepthTex = DynamicTexture2D.CreateMipmapped(screenWidth, screenHeight, PixelInternalFormat.R32f, mipLevels, debugName: "HZBDepth");
        hzbFboId = GL.GenFramebuffer();

        isInitialized = true;
        currentBufferIndex = 0;

        capi.Logger.Notification(
            $"[LumOn] Created buffers: {probeCountX}x{probeCountY} probes, " +
            $"spacing={cfg.ProbeSpacingPx}px, halfRes={halfResWidth}x{halfResHeight}");
    }

    /// <summary>
    /// Creates/recreates the temporal output FBO with MRT.
    /// Outputs to: current radiance0, current radiance1, current meta
    /// </summary>
    private void CreateTemporalOutputFbo()
    {
        // Dispose existing temporal output FBO (but not the textures - they're owned by other FBOs)
        temporalOutputFbo?.Dispose();
        temporalOutputFbo = null;

        if (radianceCurrentTex0 == null || radianceCurrentTex1 == null || probeMetaCurrentTex == null)
            return;

        // Create MRT FBO: temporal pass writes to CURRENT buffers
        // - Reads from history (previous frame's output)
        // - Writes to current (this frame's output)
        // After swap, current becomes history for next frame
        temporalOutputFbo = Rendering.GBuffer.CreateMRT(
            [radianceCurrentTex0, radianceCurrentTex1, probeMetaCurrentTex],
            null,
            ownsTextures: false,  // Textures owned by radianceCurrentFbo and probeMetaCurrentFbo
            debugName: "TemporalOutputFBO"
        );
    }

    private void DeleteBuffers()
    {
        // Dispose temporal output FBO first (doesn't own textures)
        temporalOutputFbo?.Dispose();
        temporalOutputFbo = null;

        // Dispose FBOs (they don't own textures)
        probeAnchorFbo?.Dispose();
        radianceTraceFbo?.Dispose();
        radianceCurrentFbo?.Dispose();
        radianceHistoryFbo?.Dispose();
        probeMetaCurrentFbo?.Dispose();
        probeMetaHistoryFbo?.Dispose();
        indirectHalfFbo?.Dispose();
        indirectFullFbo?.Dispose();
        capturedSceneFbo?.Dispose();
        velocityFbo?.Dispose();
        screenProbeAtlasTraceFbo?.Dispose();
        screenProbeAtlasCurrentFbo?.Dispose();
        screenProbeAtlasHistoryFbo?.Dispose();
        screenProbeAtlasFilteredFbo?.Dispose();
        probeSh9Fbo?.Dispose();

        if (hzbFboId != 0)
        {
            GL.DeleteFramebuffer(hzbFboId);
            hzbFboId = 0;
        }

        probeAnchorFbo = null;
        radianceTraceFbo = null;
        radianceCurrentFbo = null;
        radianceHistoryFbo = null;
        probeMetaCurrentFbo = null;
        probeMetaHistoryFbo = null;
        indirectHalfFbo = null;
        indirectFullFbo = null;
        capturedSceneFbo = null;
        velocityFbo = null;
        screenProbeAtlasTraceFbo = null;
        screenProbeAtlasCurrentFbo = null;
        screenProbeAtlasHistoryFbo = null;
        screenProbeAtlasFilteredFbo = null;
        probeSh9Fbo = null;

        // Dispose 2D textures
        probeAnchorPositionTex?.Dispose();
        probeAnchorNormalTex?.Dispose();
        radianceTraceTex0?.Dispose();
        radianceTraceTex1?.Dispose();
        radianceCurrentTex0?.Dispose();
        radianceCurrentTex1?.Dispose();
        radianceHistoryTex0?.Dispose();
        radianceHistoryTex1?.Dispose();
        probeMetaCurrentTex?.Dispose();
        probeMetaHistoryTex?.Dispose();
        indirectHalfTex?.Dispose();
        indirectFullTex?.Dispose();
        capturedSceneTex?.Dispose();
        velocityTex?.Dispose();
        screenProbeAtlasTraceTex?.Dispose();
        screenProbeAtlasCurrentTex?.Dispose();
        screenProbeAtlasHistoryTex?.Dispose();
        screenProbeAtlasFilteredTex?.Dispose();
        screenProbeAtlasMetaTraceTex?.Dispose();
        screenProbeAtlasMetaCurrentTex?.Dispose();
        screenProbeAtlasMetaHistoryTex?.Dispose();
        screenProbeAtlasMetaFilteredTex?.Dispose();

        probeSh9Tex0?.Dispose();
        probeSh9Tex1?.Dispose();
        probeSh9Tex2?.Dispose();
        probeSh9Tex3?.Dispose();
        probeSh9Tex4?.Dispose();
        probeSh9Tex5?.Dispose();
        probeSh9Tex6?.Dispose();

        hzbDepthTex?.Dispose();

        probeAnchorPositionTex = null;
        probeAnchorNormalTex = null;
        radianceTraceTex0 = null;
        radianceTraceTex1 = null;
        radianceCurrentTex0 = null;
        radianceCurrentTex1 = null;
        radianceHistoryTex0 = null;
        radianceHistoryTex1 = null;
        probeMetaCurrentTex = null;
        probeMetaHistoryTex = null;
        indirectHalfTex = null;
        indirectFullTex = null;
        capturedSceneTex = null;
        velocityTex = null;
        screenProbeAtlasTraceTex = null;
        screenProbeAtlasCurrentTex = null;
        screenProbeAtlasHistoryTex = null;
        screenProbeAtlasFilteredTex = null;
        screenProbeAtlasMetaTraceTex = null;
        screenProbeAtlasMetaCurrentTex = null;
        screenProbeAtlasMetaHistoryTex = null;
        screenProbeAtlasMetaFilteredTex = null;

        probeSh9Tex0 = null;
        probeSh9Tex1 = null;
        probeSh9Tex2 = null;
        probeSh9Tex3 = null;
        probeSh9Tex4 = null;
        probeSh9Tex5 = null;
        probeSh9Tex6 = null;

        hzbDepthTex = null;

        isInitialized = false;
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        DeleteBuffers();
    }

    #endregion
}
