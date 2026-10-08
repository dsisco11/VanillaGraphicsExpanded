using System;
using System.Linq;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Manages GPU textures for LumOn screen-probe atlas and world-probe integration.
/// Creates and maintains framebuffers for:
/// - Probe anchor positions and normals
/// - Screen-probe atlas (octahedral directional cache) with temporal blending
/// - Optional probe-atlas → SH9 projection targets
/// - Indirect diffuse output at half and full resolution
/// </summary>
public sealed class LumOnBufferManager : IDisposable
{
    #region Fields

    private readonly ICoreClientAPI capi;
    private readonly VgeConfig config;
    private readonly Action unregisterResize;

    // Screen dimensions tracking
    private int lastScreenWidth;
    private int lastScreenHeight;

    // Probe grid dimensions (computed from screen size and spacing)
    private int probeCountX;
    private int probeCountY;

    // Half-resolution dimensions
    private int halfResWidth;
    private int halfResHeight;

    private LumOnTargets? targets;
    private GpuFramebufferBlitter? surfaceAlbedoBlitter;

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
    public Rendering.GpuFramebuffer? ProbeAnchorFbo => targets?.ProbeAnchorFbo;

    /// <summary>
    /// Array containing probe positions in layer 0 and normals in layer 1.
    /// </summary>
    public Texture3D? ProbeAnchors => targets?.ProbeAnchors;


    // ═══════════════════════════════════════════════════════════════
    // Probe Trace Mask
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Probe-resolution trace mask (RG32F) storing two uint bitfields.
    /// Debug label: <c>LumOn.ProbeTraceMask</c>.
    /// </summary>
    public DynamicTexture2D? ProbeTraceMaskTex => targets?.ProbeTraceMaskTex;

    /// <summary>
    /// Probe-resolution PIS importance energy (R32F).
    /// Debug label: <c>LumOn.ProbePisEnergy</c>.
    /// </summary>
    public DynamicTexture2D? ProbePisEnergyTex => targets?.ProbePisEnergyTex;

    /// <summary>
    /// FBO for the probe trace mask pass.
    /// </summary>
    public Rendering.GpuFramebuffer? ProbeTraceMaskFbo => targets?.ProbeTraceMaskFbo;

    // ═══════════════════════════════════════════════════════════════
    // Screen-Probe Atlas (2D atlas)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 2D atlas for trace output probe-atlas radiance.
    /// Layout: (probeCountX * 8, probeCountY * 8), RGBA16F.
    /// </summary>
    public DynamicTexture2D? ScreenProbeAtlasTraceTex => targets?.ScreenProbeAtlasTraceTex;

    /// <summary>
    /// 2D atlas for trace output probe-atlas meta.
    /// Format: RG32F (confidence, flagsBitsAsFloat).
    /// </summary>
    public DynamicTexture2D? ScreenProbeAtlasMetaTraceTex => targets?.ScreenProbeAtlasMetaTraceTex;

    /// <summary>
    /// FBO for probe-atlas trace output.
    /// </summary>
    public Rendering.GpuFramebuffer? ScreenProbeAtlasTraceFbo => targets?.ScreenProbeAtlasTraceFbo;

    /// <summary>
    /// 2D atlas for current frame probe-atlas radiance (after temporal blend).
    /// </summary>
    public DynamicTexture2D? ScreenProbeAtlasCurrentTex => targets?.ScreenProbeAtlasCurrentTex;

    /// <summary>
    /// 2D atlas for current frame probe-atlas meta (after temporal pass).
    /// </summary>
    public DynamicTexture2D? ScreenProbeAtlasMetaCurrentTex => targets?.ScreenProbeAtlasMetaCurrentTex;

    /// <summary>
    /// 2D atlas for filtered probe-atlas radiance (post-temporal denoise).
    /// </summary>
    public DynamicTexture2D? ScreenProbeAtlasFilteredTex => targets?.ScreenProbeAtlasFilteredTex;

    /// <summary>
    /// 2D atlas for filtered probe-atlas meta (post-temporal denoise).
    /// </summary>
    public DynamicTexture2D? ScreenProbeAtlasMetaFilteredTex => targets?.ScreenProbeAtlasMetaFilteredTex;

    /// <summary>
    /// FBO for probe-atlas filtered output.
    /// </summary>
    public Rendering.GpuFramebuffer? ScreenProbeAtlasFilteredFbo => targets?.ScreenProbeAtlasFilteredFbo;

    /// <summary>
    /// FBO for SH9 projection output (7 MRT attachments).
    /// </summary>
    public Rendering.GpuFramebuffer? ProbeSh9Fbo => targets?.ProbeSh9Fbo;

    /// <summary>Seven array layers containing the packed SH9 projection coefficients.</summary>
    public Texture3D? ProbeSh9 => targets?.ProbeSh9;


    /// <summary>
    /// FBO for probe-atlas current output.
    /// </summary>
    public Rendering.GpuFramebuffer? ScreenProbeAtlasCurrentFbo => targets?.ScreenProbeAtlasCurrentFbo;

    /// <summary>
    /// 2D atlas for history probe-atlas radiance (previous frame).
    /// </summary>
    public DynamicTexture2D? ScreenProbeAtlasHistoryTex => targets?.ScreenProbeAtlasHistoryTex;

    /// <summary>
    /// 2D atlas for history probe-atlas meta (previous frame).
    /// </summary>
    public DynamicTexture2D? ScreenProbeAtlasMetaHistoryTex => targets?.ScreenProbeAtlasMetaHistoryTex;

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
    // Indirect Diffuse Output Buffers
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// FBO for half-resolution indirect diffuse output.
    /// </summary>
    public Rendering.GpuFramebuffer? IndirectHalfFbo => targets?.IndirectHalfFbo;

    /// <summary>
    /// Texture for half-resolution indirect diffuse.
    /// </summary>
    public DynamicTexture2D? IndirectHalfTex => targets?.IndirectHalfTex;

    /// <summary>
    /// FBO for full-resolution indirect diffuse output.
    /// </summary>
    public Rendering.GpuFramebuffer? IndirectFullFbo => targets?.IndirectFullFbo;

    /// <summary>
    /// Texture for full-resolution indirect diffuse (final output).
    /// </summary>
    public DynamicTexture2D? IndirectFullTex => targets?.IndirectFullTex;

    /// <summary>True only after the current render callback completed all indirect-lighting passes.</summary>
    internal bool HasPublishedIndirect { get; set; }

    // ═══════════════════════════════════════════════════════════════
    // Surface Capture Buffers
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// FBO for the captured surface albedo (used for blitting from the primary attachment).
    /// </summary>
    public Rendering.GpuFramebuffer? SurfaceAlbedoFbo => targets?.SurfaceAlbedoFbo;

    /// <summary>
    /// Captured surface albedo sampled by screen-probe ray hits.
    /// </summary>
    public DynamicTexture2D? SurfaceAlbedoTex => targets?.SurfaceAlbedoTex;

    /// <summary>
    /// FBO for velocity output (full resolution).
    /// </summary>
    public Rendering.GpuFramebuffer? VelocityFbo => targets?.VelocityFbo;

    /// <summary>
    /// Velocity texture (RGBA32F): RG = velocityUv, A = packed flags.
    /// </summary>
    public DynamicTexture2D? VelocityTex => targets?.VelocityTex;

    /// <summary>
    /// HZB depth pyramid texture (mipmapped R32F), mip 0 matches screen size.
    /// </summary>
    public DynamicTexture2D? HzbDepthTex => targets?.HzbDepthTex;

    /// <summary>
    /// FBO used for rendering into HZB mip levels.
    /// </summary>
    public Rendering.GpuFramebuffer? HzbFbo => targets?.HzbFbo;

    /// <summary>
    /// FBO id used for rendering into HZB mip levels.
    /// </summary>
    public int HzbFboId => targets?.HzbFbo?.FboId ?? 0;

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

    /// <summary>Monotonic version of storage allocation and history invalidation.</summary>
    internal int HistoryRevision { get; private set; }

    /// <summary>Borrowed comparison output, published only after a complete paired frame.</summary>
    internal GpuTexture? WorldProbeSuppressedLighting { get; set; }

    #endregion

    #region Constructor

    /// <summary>Registers screen-resource rebuilding while retaining LumOn history policy.</summary>
    public LumOnBufferManager(ICoreClientAPI capi, VgeConfig config)
    {
        this.capi = capi;
        this.config = config;
        unregisterResize = ScreenResourceManager.Register(
            ScreenResourceManager.LumOnOrder,
            OnScreenResized);
    }

    /// <summary>Recreates storage using published engine dimensions when required.</summary>
    private void OnScreenResized()
    {
        var primaryFb = capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        if (primaryFb is not null)
        {
            EnsureBuffers(primaryFb.Width, primaryFb.Height);
        }
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
            HistoryRevision++;
            WorldProbeSuppressedLighting = null;
            CreateBuffers(screenWidth, screenHeight);
            lastScreenWidth = screenWidth;
            lastScreenHeight = screenHeight;
            forceRecreateOnNextEnsure = false;
            return false;  // Buffers were recreated
        }
        return true;  // No change
    }

    /// <summary>Invalidates published lighting and schedules resource recreation on the next ensure.</summary>
    public void RequestRecreateBuffers(string reason)
    {
        HasPublishedIndirect = false;
        forceRecreateOnNextEnsure = true;
        capi.Logger.Debug("[LumOn] Buffer recreation requested: {0}", reason);
    }

    /// <summary>
    /// Swap current/history radiance buffers for temporal accumulation.
    /// Called after temporal pass completes.
    /// </summary>
    public void SwapRadianceBuffers()
    {
        // Swapping roles does not change collection ownership or duplicate resource registrations.
        if (targets is not null)
        {
            (targets.ScreenProbeAtlasCurrentTex, targets.ScreenProbeAtlasHistoryTex) = (targets.ScreenProbeAtlasHistoryTex, targets.ScreenProbeAtlasCurrentTex);
            (targets.ScreenProbeAtlasMetaCurrentTex, targets.ScreenProbeAtlasMetaHistoryTex) = (targets.ScreenProbeAtlasMetaHistoryTex, targets.ScreenProbeAtlasMetaCurrentTex);
            (targets.ScreenProbeAtlasCurrentFbo, targets.ScreenProbeAtlasHistoryFbo) = (targets.ScreenProbeAtlasHistoryFbo, targets.ScreenProbeAtlasCurrentFbo);
        }

        currentBufferIndex = 1 - currentBufferIndex;
    }

    /// <summary>
    /// Clears the radiance history buffer to black.
    /// Call this on first frame, after teleportation, or when history is invalidated.
    /// Forces full recomputation of radiance cache.
    /// </summary>
    public void ClearHistory()
    {
        HasPublishedIndirect = false;
        HistoryRevision++;
        WorldProbeSuppressedLighting = null;
        if (!isInitialized)
            return;

        GpuFramebuffer?[] history = [targets?.ScreenProbeAtlasTraceFbo, targets?.ScreenProbeAtlasCurrentFbo,
            targets?.ScreenProbeAtlasHistoryFbo, targets?.ScreenProbeAtlasFilteredFbo, targets?.ProbeSh9Fbo,
            targets?.IndirectHalfFbo, targets?.IndirectFullFbo, targets?.ProbeTraceMaskFbo, targets?.VelocityFbo];
        if (!GraphicsCommandContext.TryRun("LumOn.ClearHistory", [], true, commands =>
        {
            foreach (var target in history)
            {
                if (target is null) continue;
                var outputs = Enumerable.Range(0, target.ColorAttachmentCount)
                    .Select(i => new RenderPassColor(i, AttachmentLoad.Clear, Clear: ColorClearValue.Float(0, 0, 0, 0)));
                commands.BeginPass(new(target, outputs));
                commands.EndPass();
            }
        })) throw new InvalidOperationException("History clear boundary unavailable.");
        capi.Logger.Debug("[LumOn] Cleared probe history buffers");
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
    /// Captures the current primary framebuffer to LumOn's surface albedo texture.
    /// Call this before probe tracing so surface inputs are frame-consistent.
    /// </summary>
    public void CaptureSurfaceAlbedo()
    {
        if (!isInitialized || targets?.SurfaceAlbedoFbo == null)
            return;

        var source = GBufferManager.Instance?.PrimaryFramebuffer;
        if (source?.IsValid != true) return;
        surfaceAlbedoBlitter ??= new GpuFramebufferBlitter(source, targets.SurfaceAlbedoFbo);
        surfaceAlbedoBlitter.Blit();
    }

    #endregion

    #region Private Methods

    /// <summary>Resolves domain dimensions and replaces the owned target allocation.</summary>
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

        targets = new LumOnTargets(screenWidth, screenHeight, probeCountX, probeCountY, halfResWidth, halfResHeight);

        isInitialized = true;
        currentBufferIndex = 0;

        capi.Logger.Notification(
            $"[LumOn] Created buffers: {probeCountX}x{probeCountY} probes, " +
            $"spacing={cfg.ProbeSpacingPx}px, halfRes={halfResWidth}x{halfResHeight}");
    }

    /// <summary>Retires the allocation and withdraws its published lighting.</summary>
    private void DeleteBuffers()
    {
        HasPublishedIndirect = false;
        surfaceAlbedoBlitter?.Dispose();
        surfaceAlbedoBlitter = null;
        targets?.Dispose();
        targets = null;
        isInitialized = false;
    }

    #endregion

    #region IDisposable

    /// <summary>Unregisters screen notifications and releases owned targets.</summary>
    public void Dispose()
    {
        unregisterResize();
        WorldProbeSuppressedLighting = null;
        DeleteBuffers();
    }

    #endregion
}
