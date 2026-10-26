using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Collections.Generic;
using System.Linq;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Owns one allocation of screen-probe, temporal, and indirect-lighting render targets.</summary>
internal sealed class LumOnTargets : IDisposable
{
    private readonly GpuResourceCollection resources = new();
    private readonly Dictionary<GpuFramebuffer, RenderPassDesc> clearPasses = new();

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

            ProbeAnchors = resources.Own(RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = RHITextureDimension.TwoArray, Format = RHIFormat.Rgba16f, Width = System.Math.Max(1, probeCountX), Height = System.Math.Max(1, probeCountY), Depth = System.Math.Max(1, 2), MipLevels = 1, Storage = RHIStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "ProbeAnchors" }).RequireResource());
            ProbeAnchorFbo = CreateLayeredFramebuffer(ProbeAnchors, "ProbeAnchorFBO");

            // ═══════════════════════════════════════════════════════════════
            // Create Probe Trace Mask
            // ═══════════════════════════════════════════════════════════════

            ProbeTraceMaskTex = resources.Own(RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = RHITextureDimension.Two, Format = RHIFormat.Rg32f, Width = System.Math.Max(1, probeCountX), Height = System.Math.Max(1, probeCountY), MipLevels = 1, Storage = RHIStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "LumOn.ProbeTraceMask" }).RequireResource()!);

            // Debug/diagnostics: per-probe importance energy (sum of weights).
            ProbePisEnergyTex = resources.Own(RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = RHITextureDimension.Two, Format = RHIFormat.R32f, Width = System.Math.Max(1, probeCountX), Height = System.Math.Max(1, probeCountY), MipLevels = 1, Storage = RHIStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "LumOn.ProbePisEnergy" }).RequireResource()!);

            ProbeTraceMaskFbo = CreateTextureTarget([ProbeTraceMaskTex, ProbePisEnergyTex], "LumOn.ProbeTraceMaskFBO");

            // ═══════════════════════════════════════════════════════════════
            // Create Screen-Probe Atlas (2D atlas)
            // Implementation detail: octahedral direction mapping per probe tile.
            // Layout: (probeCountX * 8, probeCountY * 8) - tiled 8×8 per probe
            // RGBA16F: RGB = radiance, A = log-encoded hit distance
            // ═══════════════════════════════════════════════════════════════

            int atlasWidth = probeCountX * 8;
            int atlasHeight = probeCountY * 8;
            ScreenProbeAtlasTraceTex = resources.Own(RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = RHITextureDimension.Two, Format = RHIFormat.Rgba16f, Width = System.Math.Max(1, atlasWidth), Height = System.Math.Max(1, atlasHeight), MipLevels = 1, Storage = RHIStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "ScreenProbeAtlasTrace" }).RequireResource()!);
            ScreenProbeAtlasMetaTraceTex = resources.Own(RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = RHITextureDimension.Two, Format = RHIFormat.Rg32f, Width = System.Math.Max(1, atlasWidth), Height = System.Math.Max(1, atlasHeight), MipLevels = 1, Storage = RHIStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "ScreenProbeAtlasMetaTrace" }).RequireResource()!);
            ScreenProbeAtlasTraceFbo = CreateTextureTarget([ScreenProbeAtlasTraceTex, ScreenProbeAtlasMetaTraceTex], "ScreenProbeAtlasTraceFBO");

            ScreenProbeAtlasCurrentTex = resources.Own(RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = RHITextureDimension.Two, Format = RHIFormat.Rgba16f, Width = System.Math.Max(1, atlasWidth), Height = System.Math.Max(1, atlasHeight), MipLevels = 1, Storage = RHIStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "ScreenProbeAtlasCurrent" }).RequireResource()!);
            ScreenProbeAtlasMetaCurrentTex = resources.Own(RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = RHITextureDimension.Two, Format = RHIFormat.Rg32f, Width = System.Math.Max(1, atlasWidth), Height = System.Math.Max(1, atlasHeight), MipLevels = 1, Storage = RHIStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "ScreenProbeAtlasMetaCurrent" }).RequireResource()!);
            ScreenProbeAtlasCurrentFbo = CreateTextureTarget([ScreenProbeAtlasCurrentTex, ScreenProbeAtlasMetaCurrentTex], "ScreenProbeAtlasCurrentFBO");

            ScreenProbeAtlasHistoryTex = resources.Own(RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = RHITextureDimension.Two, Format = RHIFormat.Rgba16f, Width = System.Math.Max(1, atlasWidth), Height = System.Math.Max(1, atlasHeight), MipLevels = 1, Storage = RHIStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "ScreenProbeAtlasHistory" }).RequireResource()!);
            ScreenProbeAtlasMetaHistoryTex = resources.Own(RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = RHITextureDimension.Two, Format = RHIFormat.Rg32f, Width = System.Math.Max(1, atlasWidth), Height = System.Math.Max(1, atlasHeight), MipLevels = 1, Storage = RHIStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "ScreenProbeAtlasMetaHistory" }).RequireResource()!);
            ScreenProbeAtlasHistoryFbo = CreateTextureTarget([ScreenProbeAtlasHistoryTex, ScreenProbeAtlasMetaHistoryTex], "ScreenProbeAtlasHistoryFBO");

            // Filtered atlas output (Pass 3.5): derived from temporal output each frame
            ScreenProbeAtlasFilteredTex = resources.Own(RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = RHITextureDimension.Two, Format = RHIFormat.Rgba16f, Width = System.Math.Max(1, atlasWidth), Height = System.Math.Max(1, atlasHeight), MipLevels = 1, Storage = RHIStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "ScreenProbeAtlasFiltered" }).RequireResource()!);
            ScreenProbeAtlasMetaFilteredTex = resources.Own(RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = RHITextureDimension.Two, Format = RHIFormat.Rg32f, Width = System.Math.Max(1, atlasWidth), Height = System.Math.Max(1, atlasHeight), MipLevels = 1, Storage = RHIStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "ScreenProbeAtlasMetaFiltered" }).RequireResource()!);
            ScreenProbeAtlasFilteredFbo = CreateTextureTarget([ScreenProbeAtlasFilteredTex, ScreenProbeAtlasMetaFilteredTex], "ScreenProbeAtlasFilteredFBO");

            // Probe-atlas → SH9 projection output (Option B)
            // 7 RGBA16F attachments to pack 27 floats (9 RGB coeffs)
            ProbeSh9 = resources.Own(RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = RHITextureDimension.TwoArray, Format = RHIFormat.Rgba16f, Width = System.Math.Max(1, probeCountX), Height = System.Math.Max(1, probeCountY), Depth = System.Math.Max(1, 7), MipLevels = 1, Storage = RHIStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "ProbeSH9" }).RequireResource());
            ProbeSh9Fbo = CreateLayeredFramebuffer(ProbeSh9, "ProbeSH9FBO");

            // ═══════════════════════════════════════════════════════════════
            // Create Indirect Diffuse Output Buffers
            // ═══════════════════════════════════════════════════════════════

            IndirectHalfTex = resources.Own(RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = RHITextureDimension.Two, Format = RHIFormat.Rgba16f, Width = System.Math.Max(1, halfResWidth), Height = System.Math.Max(1, halfResHeight), MipLevels = 1, Storage = RHIStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "IndirectHalf" }).RequireResource()!);
            IndirectHalfFbo = CreateTextureTarget([IndirectHalfTex], "IndirectHalfFBO");

            IndirectFullTex = resources.Own(RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = RHITextureDimension.Two, Format = RHIFormat.Rgba16f, Width = System.Math.Max(1, screenWidth), Height = System.Math.Max(1, screenHeight), MipLevels = 1, Storage = RHIStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "IndirectFull" }).RequireResource()!);
            IndirectFullFbo = CreateTextureTarget([IndirectFullTex], "IndirectFullFBO");

            // ═══════════════════════════════════════════════════════════════
            // Create LumOn-owned surface capture buffers.
            // ═══════════════════════════════════════════════════════════════

            SurfaceAlbedoTex = resources.Own(RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = RHITextureDimension.Two, Format = RHIFormat.Rgba16f, Width = System.Math.Max(1, screenWidth), Height = System.Math.Max(1, screenHeight), MipLevels = 1, Storage = RHIStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = "LumOn.SurfaceAlbedo" }).RequireResource()!);
            SurfaceAlbedoFbo = CreateTextureTarget([SurfaceAlbedoTex], "LumOn.SurfaceAlbedoFBO");

            // ═══════════════════════════════════════════════════════════════
            // Velocity Buffer
            // NOTE: Packed uintBitsToFloat flags require a 32-bit float channel.
            // We use RGBA32F for simplicity and correctness.
            // ═══════════════════════════════════════════════════════════════

            VelocityTex = resources.Own(RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = RHITextureDimension.Two, Format = RHIFormat.Rgba32f, Width = System.Math.Max(1, screenWidth), Height = System.Math.Max(1, screenHeight), MipLevels = 1, Storage = RHIStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Velocity" }).RequireResource()!);
            VelocityFbo = CreateTextureTarget([VelocityTex], "VelocityFBO");

            // ═══════════════════════════════════════════════════════════════

        }
        catch { resources.Dispose(); throw; }
    }
    #endregion

    #region Internal API
    /// <summary>Selects a declaration tied to the physical allocation, independently of its current temporal role.</summary>
    internal RenderPassDesc GetPass(GpuFramebuffer target, bool clear)
        => clear ? clearPasses[target] : ((StaticGpuFramebuffer)target).PreservePass;
    #endregion

    #region Private
    /// <summary>Registers borrowed image metadata before creating the fixed texture target.</summary>
    private StaticGpuFramebuffer CreateTextureTarget(DynamicTexture2D[] textures, string name)
    {
        var images = textures.Select(texture => resources.Own(GpuFramebufferAttachment.FromTexture(texture))).ToArray();
        return CreateTarget(images, name);
    }

    /// <summary>Owns an immutable target and certifies its separate zero-clear contract during allocation.</summary>
    private StaticGpuFramebuffer CreateTarget(GpuFramebufferAttachment[] images, string name)
    {
        var target = resources.Own(StaticGpuFramebuffer.Create(images, debugName: name));
        clearPasses.Add(target, new(target, Enumerable.Range(0, images.Length)
            .Select(slot => new RenderPassColor(slot, AttachmentLoad.Clear, Clear: ColorClearValue.Float(0, 0, 0, 0)))));
        return target;
    }

    /// <summary>Routes one existing fragment output to each owned array layer.</summary>
    private GpuFramebuffer CreateLayeredFramebuffer(Texture3D texture, string name)
    {
        var attachments = new GpuFramebufferAttachment[texture.Depth];
        for (int layer = 0; layer < attachments.Length; layer++)
            attachments[layer] = resources.Own(GpuFramebufferAttachment.FromTexture(texture, layer: layer));
        return CreateTarget(attachments, name);
    }
    #endregion

    #region Lifetime
    /// <summary>Releases every allocation, regardless of its current temporal role.</summary>
    public void Dispose()
    {
        resources.Dispose();
        clearPasses.Clear();
    }
    #endregion
}
