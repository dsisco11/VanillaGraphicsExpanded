using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Owns one allocation of screen-probe, temporal, and indirect-lighting render targets.</summary>
internal sealed class LumOnTargets : IDisposable
{
    private readonly GpuResourceCollection resources = new();

    #region Typed targets
    public DynamicTexture2D ProbeAnchorPositionTex { get; }
    public DynamicTexture2D ProbeAnchorNormalTex { get; }
    public GpuFramebuffer ProbeAnchorFbo { get; }
    public DynamicTexture2D ProbeTraceMaskTex { get; }
    public DynamicTexture2D ProbePisEnergyTex { get; }
    public GpuFramebuffer ProbeTraceMaskFbo { get; }
    public DynamicTexture2D ScreenProbeAtlasTraceTex { get; }
    public GpuFramebuffer ScreenProbeAtlasTraceFbo { get; }
    public DynamicTexture2D ScreenProbeAtlasMetaTraceTex { get; }
    public DynamicTexture2D ScreenProbeAtlasCurrentTex { get; internal set; }
    public GpuFramebuffer ScreenProbeAtlasCurrentFbo { get; internal set; }
    public DynamicTexture2D ScreenProbeAtlasMetaCurrentTex { get; internal set; }
    public DynamicTexture2D ScreenProbeAtlasHistoryTex { get; internal set; }
    public GpuFramebuffer ScreenProbeAtlasHistoryFbo { get; internal set; }
    public DynamicTexture2D ScreenProbeAtlasMetaHistoryTex { get; internal set; }
    public DynamicTexture2D ScreenProbeAtlasFilteredTex { get; }
    public DynamicTexture2D ScreenProbeAtlasMetaFilteredTex { get; }
    public GpuFramebuffer ScreenProbeAtlasFilteredFbo { get; }
    public DynamicTexture2D ProbeSh9Tex0 { get; }
    public DynamicTexture2D ProbeSh9Tex1 { get; }
    public DynamicTexture2D ProbeSh9Tex2 { get; }
    public DynamicTexture2D ProbeSh9Tex3 { get; }
    public DynamicTexture2D ProbeSh9Tex4 { get; }
    public DynamicTexture2D ProbeSh9Tex5 { get; }
    public DynamicTexture2D ProbeSh9Tex6 { get; }
    public GpuFramebuffer ProbeSh9Fbo { get; }
    public DynamicTexture2D IndirectHalfTex { get; }
    public GpuFramebuffer IndirectHalfFbo { get; }
    public DynamicTexture2D IndirectFullTex { get; }
    public GpuFramebuffer IndirectFullFbo { get; }
    public DynamicTexture2D SurfaceAlbedoTex { get; }
    public GpuFramebuffer SurfaceAlbedoFbo { get; }
    public DynamicTexture2D VelocityTex { get; }
    public GpuFramebuffer VelocityFbo { get; }
    public DynamicTexture2D HzbDepthTex { get; }
    public GpuFramebuffer HzbFbo { get; }
    #endregion

    #region Allocation
    /// <summary>Allocates targets using the manager's resolved grid and output dimensions.</summary>
    public LumOnTargets(int screenWidth, int screenHeight, int probeCountX, int probeCountY, int halfResWidth, int halfResHeight)
    {
        // Track each allocation before constructing dependent FBOs so partial construction is reclaimable.
        try
        {
            // ═══════════════════════════════════════════════════════════════
            // Create Probe Anchor Buffers
            // ═══════════════════════════════════════════════════════════════

            ProbeAnchorPositionTex = resources.Own(DynamicTexture2D.Create(probeCountX, probeCountY, PixelInternalFormat.Rgba16f, debugName: "ProbeAnchorPosition")!);
            ProbeAnchorNormalTex = resources.Own(DynamicTexture2D.Create(probeCountX, probeCountY, PixelInternalFormat.Rgba16f, debugName: "ProbeAnchorNormal")!);
            ProbeAnchorFbo = resources.Own(GpuFramebuffer.CreateMRT("ProbeAnchorFBO", ProbeAnchorPositionTex, ProbeAnchorNormalTex)!);

            // ═══════════════════════════════════════════════════════════════
            // Create Probe Trace Mask
            // ═══════════════════════════════════════════════════════════════

            ProbeTraceMaskTex = resources.Own(DynamicTexture2D.Create(
                probeCountX,
                probeCountY,
                PixelInternalFormat.Rg32f,
                TextureFilterMode.Nearest,
                debugName: "LumOn.ProbeTraceMask")!);

            // Debug/diagnostics: per-probe importance energy (sum of weights).
            ProbePisEnergyTex = resources.Own(DynamicTexture2D.Create(
                probeCountX,
                probeCountY,
                PixelInternalFormat.R32f,
                TextureFilterMode.Nearest,
                debugName: "LumOn.ProbePisEnergy")!);

            ProbeTraceMaskFbo = resources.Own(GpuFramebuffer.CreateMRT(
                [ProbeTraceMaskTex, ProbePisEnergyTex],
                depthTexture: null,
                debugName: "LumOn.ProbeTraceMaskFBO")!);

            // ═══════════════════════════════════════════════════════════════
            // Create Screen-Probe Atlas (2D atlas)
            // Implementation detail: octahedral direction mapping per probe tile.
            // Layout: (probeCountX * 8, probeCountY * 8) - tiled 8×8 per probe
            // RGBA16F: RGB = radiance, A = log-encoded hit distance
            // ═══════════════════════════════════════════════════════════════

            int atlasWidth = probeCountX * 8;
            int atlasHeight = probeCountY * 8;
            ScreenProbeAtlasTraceTex = resources.Own(DynamicTexture2D.Create(atlasWidth, atlasHeight, PixelInternalFormat.Rgba16f, debugName: "ScreenProbeAtlasTrace")!);
            ScreenProbeAtlasMetaTraceTex = resources.Own(DynamicTexture2D.Create(atlasWidth, atlasHeight, PixelInternalFormat.Rg32f, debugName: "ScreenProbeAtlasMetaTrace")!);
            ScreenProbeAtlasTraceFbo = resources.Own(GpuFramebuffer.CreateMRT([ScreenProbeAtlasTraceTex, ScreenProbeAtlasMetaTraceTex], depthTexture: null, debugName: "ScreenProbeAtlasTraceFBO")!);

            ScreenProbeAtlasCurrentTex = resources.Own(DynamicTexture2D.Create(atlasWidth, atlasHeight, PixelInternalFormat.Rgba16f, debugName: "ScreenProbeAtlasCurrent")!);
            ScreenProbeAtlasMetaCurrentTex = resources.Own(DynamicTexture2D.Create(atlasWidth, atlasHeight, PixelInternalFormat.Rg32f, debugName: "ScreenProbeAtlasMetaCurrent")!);
            ScreenProbeAtlasCurrentFbo = resources.Own(GpuFramebuffer.CreateMRT([ScreenProbeAtlasCurrentTex, ScreenProbeAtlasMetaCurrentTex], depthTexture: null, debugName: "ScreenProbeAtlasCurrentFBO")!);

            ScreenProbeAtlasHistoryTex = resources.Own(DynamicTexture2D.Create(atlasWidth, atlasHeight, PixelInternalFormat.Rgba16f, debugName: "ScreenProbeAtlasHistory")!);
            ScreenProbeAtlasMetaHistoryTex = resources.Own(DynamicTexture2D.Create(atlasWidth, atlasHeight, PixelInternalFormat.Rg32f, debugName: "ScreenProbeAtlasMetaHistory")!);
            ScreenProbeAtlasHistoryFbo = resources.Own(GpuFramebuffer.CreateMRT([ScreenProbeAtlasHistoryTex, ScreenProbeAtlasMetaHistoryTex], depthTexture: null, debugName: "ScreenProbeAtlasHistoryFBO")!);

            // Filtered atlas output (Pass 3.5): derived from temporal output each frame
            ScreenProbeAtlasFilteredTex = resources.Own(DynamicTexture2D.Create(atlasWidth, atlasHeight, PixelInternalFormat.Rgba16f, debugName: "ScreenProbeAtlasFiltered")!);
            ScreenProbeAtlasMetaFilteredTex = resources.Own(DynamicTexture2D.Create(atlasWidth, atlasHeight, PixelInternalFormat.Rg32f, debugName: "ScreenProbeAtlasMetaFiltered")!);
            ScreenProbeAtlasFilteredFbo = resources.Own(GpuFramebuffer.CreateMRT([ScreenProbeAtlasFilteredTex, ScreenProbeAtlasMetaFilteredTex], depthTexture: null, debugName: "ScreenProbeAtlasFilteredFBO")!);

            // Probe-atlas → SH9 projection output (Option B)
            // 7 RGBA16F attachments to pack 27 floats (9 RGB coeffs)
            ProbeSh9Tex0 = resources.Own(DynamicTexture2D.Create(probeCountX, probeCountY, PixelInternalFormat.Rgba16f, debugName: "ProbeSH9_0")!);
            ProbeSh9Tex1 = resources.Own(DynamicTexture2D.Create(probeCountX, probeCountY, PixelInternalFormat.Rgba16f, debugName: "ProbeSH9_1")!);
            ProbeSh9Tex2 = resources.Own(DynamicTexture2D.Create(probeCountX, probeCountY, PixelInternalFormat.Rgba16f, debugName: "ProbeSH9_2")!);
            ProbeSh9Tex3 = resources.Own(DynamicTexture2D.Create(probeCountX, probeCountY, PixelInternalFormat.Rgba16f, debugName: "ProbeSH9_3")!);
            ProbeSh9Tex4 = resources.Own(DynamicTexture2D.Create(probeCountX, probeCountY, PixelInternalFormat.Rgba16f, debugName: "ProbeSH9_4")!);
            ProbeSh9Tex5 = resources.Own(DynamicTexture2D.Create(probeCountX, probeCountY, PixelInternalFormat.Rgba16f, debugName: "ProbeSH9_5")!);
            ProbeSh9Tex6 = resources.Own(DynamicTexture2D.Create(probeCountX, probeCountY, PixelInternalFormat.Rgba16f, debugName: "ProbeSH9_6")!);
            ProbeSh9Fbo = resources.Own(GpuFramebuffer.CreateMRT(
                [ProbeSh9Tex0, ProbeSh9Tex1, ProbeSh9Tex2, ProbeSh9Tex3, ProbeSh9Tex4, ProbeSh9Tex5, ProbeSh9Tex6],
                depthTexture: null,
                debugName: "ProbeSH9FBO")!);

            // ═══════════════════════════════════════════════════════════════
            // Create Indirect Diffuse Output Buffers
            // ═══════════════════════════════════════════════════════════════

            IndirectHalfTex = resources.Own(DynamicTexture2D.Create(halfResWidth, halfResHeight, PixelInternalFormat.Rgba16f, debugName: "IndirectHalf")!);
            IndirectHalfFbo = resources.Own(GpuFramebuffer.CreateSingle(IndirectHalfTex, debugName: "IndirectHalfFBO")!);

            IndirectFullTex = resources.Own(DynamicTexture2D.Create(screenWidth, screenHeight, PixelInternalFormat.Rgba16f, debugName: "IndirectFull")!);
            IndirectFullFbo = resources.Own(GpuFramebuffer.CreateSingle(IndirectFullTex, debugName: "IndirectFullFBO")!);

            // ═══════════════════════════════════════════════════════════════
            // Create LumOn-owned surface capture buffers.
            // ═══════════════════════════════════════════════════════════════

            SurfaceAlbedoTex = resources.Own(DynamicTexture2D.Create(screenWidth, screenHeight, PixelInternalFormat.Rgba16f, TextureFilterMode.Linear, debugName: "LumOn.SurfaceAlbedo")!);
            SurfaceAlbedoFbo = resources.Own(GpuFramebuffer.CreateSingle(SurfaceAlbedoTex, debugName: "LumOn.SurfaceAlbedoFBO")!);

            // ═══════════════════════════════════════════════════════════════
            // Velocity Buffer
            // NOTE: Packed uintBitsToFloat flags require a 32-bit float channel.
            // We use RGBA32F for simplicity and correctness.
            // ═══════════════════════════════════════════════════════════════

            VelocityTex = resources.Own(DynamicTexture2D.Create(screenWidth, screenHeight, PixelInternalFormat.Rgba32f, TextureFilterMode.Nearest, debugName: "Velocity")!);
            VelocityFbo = resources.Own(GpuFramebuffer.CreateSingle(VelocityTex, debugName: "VelocityFBO")!);

            // ═══════════════════════════════════════════════════════════════
            // HZB Depth Pyramid (mipmapped R32F)
            // ═══════════════════════════════════════════════════════════════

            int maxDim = Math.Max(screenWidth, screenHeight);
            int mipLevels = 1;
            while ((maxDim >>= 1) > 0) mipLevels++;

            HzbDepthTex = resources.Own(DynamicTexture2D.CreateMipmapped(screenWidth, screenHeight, PixelInternalFormat.R32f, mipLevels, debugName: "HZBDepth")!);
            HzbFbo = resources.Own(GpuFramebuffer.CreateEmpty(debugName: "HZBFBO")!);

        }
        catch { resources.Dispose(); throw; }
    }
    #endregion

    #region Lifetime
    /// <summary>Releases every allocation, regardless of its current temporal role.</summary>
    public void Dispose() => resources.Dispose();
    #endregion
}
