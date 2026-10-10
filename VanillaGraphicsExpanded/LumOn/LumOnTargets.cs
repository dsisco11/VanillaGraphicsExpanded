using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Owns one allocation of screen-probe, temporal, and indirect-lighting render targets.</summary>
internal sealed class LumOnTargets : IDisposable
{
    private readonly GpuResourceCollection resources = new();

    #region Typed targets
    /// <summary>Owns position and normal layers for each screen probe.</summary>
    public Texture3D ProbeAnchors { get; }
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
    /// <summary>Owns seven packed SH9 coefficient layers.</summary>
    public Texture3D ProbeSh9 { get; }
    public GpuFramebuffer ProbeSh9Fbo { get; }
    public DynamicTexture2D IndirectHalfTex { get; }
    public GpuFramebuffer IndirectHalfFbo { get; }
    public DynamicTexture2D IndirectFullTex { get; }
    public GpuFramebuffer IndirectFullFbo { get; }
    public DynamicTexture2D SurfaceAlbedoTex { get; }
    public GpuFramebuffer SurfaceAlbedoFbo { get; }
    public DynamicTexture2D VelocityTex { get; }
    public GpuFramebuffer VelocityFbo { get; }
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

            ProbeAnchors = resources.Own(Texture3D.Create(probeCountX, probeCountY, 2, PixelInternalFormat.Rgba16f,
                TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "ProbeAnchors"));
            ProbeAnchorFbo = CreateLayeredFramebuffer(ProbeAnchors, "ProbeAnchorFBO");

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
            ProbeSh9 = resources.Own(Texture3D.Create(probeCountX, probeCountY, 7, PixelInternalFormat.Rgba16f,
                TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "ProbeSH9"));
            ProbeSh9Fbo = CreateLayeredFramebuffer(ProbeSh9, "ProbeSH9FBO");

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

        }
        catch { resources.Dispose(); throw; }
    }
    #endregion

    #region Private
    /// <summary>Routes one existing fragment output to each owned array layer.</summary>
    private GpuFramebuffer CreateLayeredFramebuffer(Texture3D texture, string name)
    {
        var attachments = new GpuFramebufferAttachment[texture.Depth];
        for (int layer = 0; layer < attachments.Length; layer++)
            attachments[layer] = resources.Own(GpuFramebufferAttachment.FromTexture(texture, layer: layer));
        return resources.Own(GpuFramebuffer.Create(attachments, debugName: name));
    }
    #endregion

    #region Lifetime
    /// <summary>Releases every allocation, regardless of its current temporal role.</summary>
    public void Dispose() => resources.Dispose();
    #endregion
}
