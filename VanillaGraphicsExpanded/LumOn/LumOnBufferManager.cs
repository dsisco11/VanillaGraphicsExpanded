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

    /// <summary>
    /// OpenGL FBO ID for probe anchor pass output.
    /// </summary>
    public int ProbeAnchorFboId => probeAnchorFbo?.FboId ?? 0;

    /// <summary>
    /// OpenGL texture ID for probe anchor positions (posVS.xyz, valid).
    /// </summary>
    public int ProbeAnchorPositionTextureId => probeAnchorPositionTex?.TextureId ?? 0;

    /// <summary>
    /// OpenGL texture ID for probe anchor normals (normalVS.xyz, reserved).
    /// </summary>
    public int ProbeAnchorNormalTextureId => probeAnchorNormalTex?.TextureId ?? 0;

    /// <summary>
    /// OpenGL FBO ID for trace pass radiance output.
    /// </summary>
    public int RadianceTraceFboId => radianceTraceFbo?.FboId ?? 0;

    /// <summary>
    /// OpenGL texture ID for trace radiance SH coefficients (set 0).
    /// </summary>
    public int RadianceTraceTexture0Id => radianceTraceTex0?.TextureId ?? 0;

    /// <summary>
    /// OpenGL texture ID for trace radiance SH coefficients (set 1).
    /// </summary>
    public int RadianceTraceTexture1Id => radianceTraceTex1?.TextureId ?? 0;

    /// <summary>
    /// OpenGL FBO ID for current frame radiance output.
    /// </summary>
    public int RadianceCurrentFboId => radianceCurrentFbo?.FboId ?? 0;

    /// <summary>
    /// OpenGL texture ID for current radiance SH coefficients (set 0).
    /// </summary>
    public int RadianceCurrentTexture0Id => radianceCurrentTex0?.TextureId ?? 0;

    /// <summary>
    /// OpenGL texture ID for current radiance SH coefficients (set 1).
    /// </summary>
    public int RadianceCurrentTexture1Id => radianceCurrentTex1?.TextureId ?? 0;

    /// <summary>
    /// OpenGL FBO ID for history radiance (read during temporal blend).
    /// </summary>
    public int RadianceHistoryFboId => radianceHistoryFbo?.FboId ?? 0;

    /// <summary>
    /// OpenGL texture ID for history radiance SH coefficients (set 0).
    /// </summary>
    public int RadianceHistoryTexture0Id => radianceHistoryTex0?.TextureId ?? 0;

    /// <summary>
    /// OpenGL texture ID for history radiance SH coefficients (set 1).
    /// </summary>
    public int RadianceHistoryTexture1Id => radianceHistoryTex1?.TextureId ?? 0;

    /// <summary>
    /// OpenGL FBO ID for current frame probe metadata.
    /// </summary>
    public int ProbeMetaCurrentFboId => probeMetaCurrentFbo?.FboId ?? 0;

    /// <summary>
    /// OpenGL texture ID for current frame probe metadata (depth, normal, accumCount).
    /// </summary>
    public int ProbeMetaCurrentTextureId => probeMetaCurrentTex?.TextureId ?? 0;

    /// <summary>
    /// OpenGL FBO ID for history probe metadata.
    /// </summary>
    public int ProbeMetaHistoryFboId => probeMetaHistoryFbo?.FboId ?? 0;

    /// <summary>
    /// OpenGL texture ID for history probe metadata.
    /// </summary>
    public int ProbeMetaHistoryTextureId => probeMetaHistoryTex?.TextureId ?? 0;

    /// <summary>
    /// OpenGL FBO ID for temporal pass MRT output (radiance0, radiance1, meta).
    /// </summary>
    public int TemporalOutputFboId => temporalOutputFbo?.FboId ?? 0;

    /// <summary>
    /// OpenGL FBO ID for half-resolution indirect diffuse output.
    /// </summary>
    public int IndirectHalfFboId => indirectHalfFbo?.FboId ?? 0;

    /// <summary>
    /// OpenGL texture ID for half-resolution indirect diffuse.
    /// </summary>
    public int IndirectHalfTextureId => indirectHalfTex?.TextureId ?? 0;

    /// <summary>
    /// OpenGL FBO ID for full-resolution indirect diffuse output.
    /// </summary>
    public int IndirectFullFboId => indirectFullFbo?.FboId ?? 0;

    /// <summary>
    /// OpenGL texture ID for full-resolution indirect diffuse (final output).
    /// </summary>
    public int IndirectFullTextureId => indirectFullTex?.TextureId ?? 0;

    /// <summary>
    /// OpenGL FBO ID for captured scene (used for blitting).
    /// </summary>
    public int CapturedSceneFboId => capturedSceneFbo?.FboId ?? 0;

    /// <summary>
    /// OpenGL texture ID for captured scene (radiance sampling source).
    /// </summary>
    public int CapturedSceneTextureId => capturedSceneTex?.TextureId ?? 0;

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
    public void EnsureBuffers(int screenWidth, int screenHeight)
    {
        if (!isInitialized || screenWidth != lastScreenWidth || screenHeight != lastScreenHeight)
        {
            CreateBuffers(screenWidth, screenHeight);
            lastScreenWidth = screenWidth;
            lastScreenHeight = screenHeight;
        }
    }

    /// <summary>
    /// Swap current/history radiance buffers for temporal accumulation.
    /// Called after temporal pass completes.
    /// </summary>
    public void SwapRadianceBuffers()
    {
        // Swap radiance textures
        (radianceCurrentTex0, radianceHistoryTex0) = (radianceHistoryTex0, radianceCurrentTex0);
        (radianceCurrentTex1, radianceHistoryTex1) = (radianceHistoryTex1, radianceCurrentTex1);
        (radianceCurrentFbo, radianceHistoryFbo) = (radianceHistoryFbo, radianceCurrentFbo);

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

        // Clear all radiance and metadata buffers to black
        radianceHistoryFbo?.BindAndClear();
        radianceCurrentFbo?.BindAndClear();
        probeMetaHistoryFbo?.BindAndClear();
        probeMetaCurrentFbo?.BindAndClear();

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

        probeAnchorFbo = null;
        radianceTraceFbo = null;
        radianceCurrentFbo = null;
        radianceHistoryFbo = null;
        probeMetaCurrentFbo = null;
        probeMetaHistoryFbo = null;
        indirectHalfFbo = null;
        indirectFullFbo = null;
        capturedSceneFbo = null;

        // Dispose textures
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
