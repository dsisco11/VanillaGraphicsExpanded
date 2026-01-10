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

    private DynamicTexture? probeAnchorPositionTex;
    private DynamicTexture? probeAnchorNormalTex;
    private Rendering.GBuffer? probeAnchorFbo;

    // ═══════════════════════════════════════════════════════════════
    // Radiance Cache Buffers (Triple-buffered for temporal)
    // ═══════════════════════════════════════════════════════════════

    // Trace output radiance (written by trace pass, read by temporal pass)
    private DynamicTexture? radianceTraceTex0;
    private DynamicTexture? radianceTraceTex1;
    private Rendering.GBuffer? radianceTraceFbo;

    // Current frame radiance (written by temporal pass, read by gather)
    private DynamicTexture? radianceCurrentTex0;
    private DynamicTexture? radianceCurrentTex1;
    private Rendering.GBuffer? radianceCurrentFbo;

    // History radiance (previous frame's current, read by temporal pass)
    private DynamicTexture? radianceHistoryTex0;
    private DynamicTexture? radianceHistoryTex1;
    private Rendering.GBuffer? radianceHistoryFbo;

    // ═══════════════════════════════════════════════════════════════
    // Screen-Probe Atlas (2D atlas layout)
    // Implementation detail: octahedral direction mapping per probe tile.
    // Layout: (probeCountX * 8, probeCountY * 8) - tiled 8×8 per probe
    // RGBA16F: RGB = radiance, A = log-encoded hit distance
    // ═══════════════════════════════════════════════════════════════

    // Trace output probe atlas (written by trace pass, read by temporal pass)
    private DynamicTexture? screenProbeAtlasTraceTex;
    private Rendering.GBuffer? screenProbeAtlasTraceFbo;

    // Current frame probe atlas (written by temporal pass, read by gather)
    private DynamicTexture? screenProbeAtlasCurrentTex;
    private Rendering.GBuffer? screenProbeAtlasCurrentFbo;

    // History probe atlas (previous frame's current, read by temporal pass)
    private DynamicTexture? screenProbeAtlasHistoryTex;
    private Rendering.GBuffer? screenProbeAtlasHistoryFbo;

    // ═══════════════════════════════════════════════════════════════
    // Probe Metadata Buffers (for temporal validation)
    // ═══════════════════════════════════════════════════════════════

    private DynamicTexture? probeMetaCurrentTex;
    private Rendering.GBuffer? probeMetaCurrentFbo;

    // History metadata (swapped each frame)
    private DynamicTexture? probeMetaHistoryTex;
    private Rendering.GBuffer? probeMetaHistoryFbo;

    // Temporal output FBO (MRT: radiance0, radiance1, meta to current buffers)
    private Rendering.GBuffer? temporalOutputFbo;

    // ═══════════════════════════════════════════════════════════════
    // Indirect Diffuse Output Buffers
    // ═══════════════════════════════════════════════════════════════

    private DynamicTexture? indirectHalfTex;
    private Rendering.GBuffer? indirectHalfFbo;

    private DynamicTexture? indirectFullTex;
    private Rendering.GBuffer? indirectFullFbo;

    // ═══════════════════════════════════════════════════════════════
    // Captured Scene Buffer (for radiance sampling)
    // ═══════════════════════════════════════════════════════════════

    private DynamicTexture? capturedSceneTex;
    private Rendering.GBuffer? capturedSceneFbo;

    // ═══════════════════════════════════════════════════════════════
    // Depth Pyramid / HZB
    // ═══════════════════════════════════════════════════════════════

    private DynamicTexture? hzbDepthTex;
    private int hzbFboId;

    // Double-buffer swap index (0 or 1)
    private int currentBufferIndex;

    private bool isInitialized;

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
    public DynamicTexture? ProbeAnchorPositionTex => probeAnchorPositionTex;

    /// <summary>
    /// Texture for probe anchor normals (normalVS.xyz, reserved).
    /// </summary>
    public DynamicTexture? ProbeAnchorNormalTex => probeAnchorNormalTex;

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
    public DynamicTexture? RadianceTraceTex0 => radianceTraceTex0;

    /// <summary>
    /// Texture for trace radiance SH coefficients (set 1).
    /// </summary>
    public DynamicTexture? RadianceTraceTex1 => radianceTraceTex1;

    /// <summary>
    /// FBO for current frame radiance output.
    /// </summary>
    public Rendering.GBuffer? RadianceCurrentFbo => radianceCurrentFbo;

    /// <summary>
    /// Texture for current radiance SH coefficients (set 0).
    /// </summary>
    public DynamicTexture? RadianceCurrentTex0 => radianceCurrentTex0;

    /// <summary>
    /// Texture for current radiance SH coefficients (set 1).
    /// </summary>
    public DynamicTexture? RadianceCurrentTex1 => radianceCurrentTex1;

    /// <summary>
    /// FBO for history radiance (read during temporal blend).
    /// </summary>
    public Rendering.GBuffer? RadianceHistoryFbo => radianceHistoryFbo;

    /// <summary>
    /// Texture for history radiance SH coefficients (set 0).
    /// </summary>
    public DynamicTexture? RadianceHistoryTex0 => radianceHistoryTex0;

    /// <summary>
    /// Texture for history radiance SH coefficients (set 1).
    /// </summary>
    public DynamicTexture? RadianceHistoryTex1 => radianceHistoryTex1;

    // ═══════════════════════════════════════════════════════════════
    // Screen-Probe Atlas (2D atlas)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 2D atlas for trace output probe-atlas radiance.
    /// Layout: (probeCountX * 8, probeCountY * 8), RGBA16F.
    /// </summary>
    public DynamicTexture? ScreenProbeAtlasTraceTex => screenProbeAtlasTraceTex;

    /// <summary>
    /// FBO for probe-atlas trace output.
    /// </summary>
    public Rendering.GBuffer? ScreenProbeAtlasTraceFbo => screenProbeAtlasTraceFbo;

    /// <summary>
    /// 2D atlas for current frame probe-atlas radiance (after temporal blend).
    /// </summary>
    public DynamicTexture? ScreenProbeAtlasCurrentTex => screenProbeAtlasCurrentTex;

    /// <summary>
    /// FBO for probe-atlas current output.
    /// </summary>
    public Rendering.GBuffer? ScreenProbeAtlasCurrentFbo => screenProbeAtlasCurrentFbo;

    /// <summary>
    /// 2D atlas for history probe-atlas radiance (previous frame).
    /// </summary>
    public DynamicTexture? ScreenProbeAtlasHistoryTex => screenProbeAtlasHistoryTex;

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
    public DynamicTexture? ProbeMetaCurrentTex => probeMetaCurrentTex;

    /// <summary>
    /// FBO for history probe metadata.
    /// </summary>
    public Rendering.GBuffer? ProbeMetaHistoryFbo => probeMetaHistoryFbo;

    /// <summary>
    /// Texture for history probe metadata.
    /// </summary>
    public DynamicTexture? ProbeMetaHistoryTex => probeMetaHistoryTex;

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
    public DynamicTexture? IndirectHalfTex => indirectHalfTex;

    /// <summary>
    /// FBO for full-resolution indirect diffuse output.
    /// </summary>
    public Rendering.GBuffer? IndirectFullFbo => indirectFullFbo;

    /// <summary>
    /// Texture for full-resolution indirect diffuse (final output).
    /// </summary>
    public DynamicTexture? IndirectFullTex => indirectFullTex;

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
    public DynamicTexture? CapturedSceneTex => capturedSceneTex;

    /// <summary>
    /// HZB depth pyramid texture (mipmapped R32F), mip 0 matches screen size.
    /// </summary>
    public DynamicTexture? HzbDepthTex => hzbDepthTex;

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
        if (!isInitialized || screenWidth != lastScreenWidth || screenHeight != lastScreenHeight)
        {
            CreateBuffers(screenWidth, screenHeight);
            lastScreenWidth = screenWidth;
            lastScreenHeight = screenHeight;
            return false;  // Buffers were recreated
        }
        return true;  // No change
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

        // Calculate probe grid dimensions
        probeCountX = (int)Math.Ceiling((float)screenWidth / config.ProbeSpacingPx);
        probeCountY = (int)Math.Ceiling((float)screenHeight / config.ProbeSpacingPx);

        // Calculate half-res dimensions
        halfResWidth = config.HalfResolution ? screenWidth / 2 : screenWidth;
        halfResHeight = config.HalfResolution ? screenHeight / 2 : screenHeight;

        // ═══════════════════════════════════════════════════════════════
        // Create Probe Anchor Buffers
        // ═══════════════════════════════════════════════════════════════

        probeAnchorPositionTex = DynamicTexture.Create(probeCountX, probeCountY, PixelInternalFormat.Rgba16f);
        probeAnchorNormalTex = DynamicTexture.Create(probeCountX, probeCountY, PixelInternalFormat.Rgba16f);
        probeAnchorFbo = Rendering.GBuffer.CreateMRT(probeAnchorPositionTex, probeAnchorNormalTex);

        // ═══════════════════════════════════════════════════════════════
        // Create Radiance Cache Buffers (Trace output)
        // ═══════════════════════════════════════════════════════════════

        radianceTraceTex0 = DynamicTexture.Create(probeCountX, probeCountY, PixelInternalFormat.Rgba16f);
        radianceTraceTex1 = DynamicTexture.Create(probeCountX, probeCountY, PixelInternalFormat.Rgba16f);
        radianceTraceFbo = Rendering.GBuffer.CreateMRT(radianceTraceTex0, radianceTraceTex1);

        // ═══════════════════════════════════════════════════════════════
        // Create Radiance Cache Buffers (Current - temporal output)
        // ═══════════════════════════════════════════════════════════════

        radianceCurrentTex0 = DynamicTexture.Create(probeCountX, probeCountY, PixelInternalFormat.Rgba16f);
        radianceCurrentTex1 = DynamicTexture.Create(probeCountX, probeCountY, PixelInternalFormat.Rgba16f);
        radianceCurrentFbo = Rendering.GBuffer.CreateMRT(radianceCurrentTex0, radianceCurrentTex1);

        // ═══════════════════════════════════════════════════════════════
        // Create Radiance Cache Buffers (History)
        // ═══════════════════════════════════════════════════════════════

        radianceHistoryTex0 = DynamicTexture.Create(probeCountX, probeCountY, PixelInternalFormat.Rgba16f);
        radianceHistoryTex1 = DynamicTexture.Create(probeCountX, probeCountY, PixelInternalFormat.Rgba16f);
        radianceHistoryFbo = Rendering.GBuffer.CreateMRT(radianceHistoryTex0, radianceHistoryTex1);

        // ═══════════════════════════════════════════════════════════════
        // Create Screen-Probe Atlas (2D atlas)
        // Implementation detail: octahedral direction mapping per probe tile.
        // Layout: (probeCountX * 8, probeCountY * 8) - tiled 8×8 per probe
        // RGBA16F: RGB = radiance, A = log-encoded hit distance
        // ═══════════════════════════════════════════════════════════════

        int atlasWidth = probeCountX * 8;
        int atlasHeight = probeCountY * 8;
        screenProbeAtlasTraceTex = DynamicTexture.Create(atlasWidth, atlasHeight, PixelInternalFormat.Rgba16f);
        screenProbeAtlasTraceFbo = Rendering.GBuffer.CreateSingle(screenProbeAtlasTraceTex);
        screenProbeAtlasCurrentTex = DynamicTexture.Create(atlasWidth, atlasHeight, PixelInternalFormat.Rgba16f);
        screenProbeAtlasCurrentFbo = Rendering.GBuffer.CreateSingle(screenProbeAtlasCurrentTex);
        screenProbeAtlasHistoryTex = DynamicTexture.Create(atlasWidth, atlasHeight, PixelInternalFormat.Rgba16f);
        screenProbeAtlasHistoryFbo = Rendering.GBuffer.CreateSingle(screenProbeAtlasHistoryTex);

        // ═══════════════════════════════════════════════════════════════
        // Create Probe Metadata Buffers (for temporal validation)
        // ═══════════════════════════════════════════════════════════════

        probeMetaCurrentTex = DynamicTexture.Create(probeCountX, probeCountY, PixelInternalFormat.Rgba16f);
        probeMetaCurrentFbo = Rendering.GBuffer.CreateSingle(probeMetaCurrentTex);

        probeMetaHistoryTex = DynamicTexture.Create(probeCountX, probeCountY, PixelInternalFormat.Rgba16f);
        probeMetaHistoryFbo = Rendering.GBuffer.CreateSingle(probeMetaHistoryTex);

        // Create temporal output FBO (MRT: writes to current radiance + current meta)
        CreateTemporalOutputFbo();

        // ═══════════════════════════════════════════════════════════════
        // Create Indirect Diffuse Output Buffers
        // ═══════════════════════════════════════════════════════════════

        indirectHalfTex = DynamicTexture.Create(halfResWidth, halfResHeight, PixelInternalFormat.Rgba16f);
        indirectHalfFbo = Rendering.GBuffer.CreateSingle(indirectHalfTex);

        indirectFullTex = DynamicTexture.Create(screenWidth, screenHeight, PixelInternalFormat.Rgba16f);
        indirectFullFbo = Rendering.GBuffer.CreateSingle(indirectFullTex);

        // ═══════════════════════════════════════════════════════════════
        // Create Captured Scene Buffer
        // ═══════════════════════════════════════════════════════════════

        capturedSceneTex = DynamicTexture.Create(screenWidth, screenHeight, PixelInternalFormat.Rgba16f, TextureFilterMode.Linear);
        capturedSceneFbo = Rendering.GBuffer.CreateSingle(capturedSceneTex);

        // ═══════════════════════════════════════════════════════════════
        // HZB Depth Pyramid (mipmapped R32F)
        // ═══════════════════════════════════════════════════════════════

        int maxDim = Math.Max(screenWidth, screenHeight);
        int mipLevels = 1;
        while ((maxDim >>= 1) > 0) mipLevels++;

        hzbDepthTex = DynamicTexture.CreateMipmapped(screenWidth, screenHeight, PixelInternalFormat.R32f, mipLevels);
        hzbFboId = GL.GenFramebuffer();

        isInitialized = true;
        currentBufferIndex = 0;

        capi.Logger.Notification(
            $"[LumOn] Created buffers: {probeCountX}x{probeCountY} probes, " +
            $"spacing={config.ProbeSpacingPx}px, halfRes={halfResWidth}x{halfResHeight}");
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
            ownsTextures: false  // Textures owned by radianceCurrentFbo and probeMetaCurrentFbo
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
        screenProbeAtlasTraceFbo?.Dispose();
        screenProbeAtlasCurrentFbo?.Dispose();
        screenProbeAtlasHistoryFbo?.Dispose();

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
        screenProbeAtlasTraceFbo = null;
        screenProbeAtlasCurrentFbo = null;
        screenProbeAtlasHistoryFbo = null;

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
        screenProbeAtlasTraceTex?.Dispose();
        screenProbeAtlasCurrentTex?.Dispose();
        screenProbeAtlasHistoryTex?.Dispose();

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
        screenProbeAtlasTraceTex = null;
        screenProbeAtlasCurrentTex = null;
        screenProbeAtlasHistoryTex = null;

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
