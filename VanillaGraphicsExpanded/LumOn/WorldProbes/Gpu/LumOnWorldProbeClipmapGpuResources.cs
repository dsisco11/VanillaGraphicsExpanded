using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;

/// <summary>Owns clipmap textures and their generation-scoped contents.</summary>
internal sealed partial class LumOnWorldProbeClipmapGpuResources : IDisposable
{
    private readonly int resolution;
    private readonly int levels;
    private readonly int worldProbeTileSize;
    private readonly GpuResourceCollection owned = new();

    private readonly DynamicTexture2D radianceAtlas;
    private readonly DynamicTexture2D vis0;
    private readonly DynamicTexture2D dist0;
    private readonly DynamicTexture2D meta0;

    private readonly Texture2D debugState0;

    private readonly StaticGpuFramebuffer fbo;
    private readonly StaticGpuFramebuffer radianceFbo;

    public int Resolution => resolution;
    public int Levels => levels;

    public int WorldProbeTileSize => worldProbeTileSize;

    // Per-probe scalar atlases: one texel per probe.
    public int AtlasWidth => resolution * resolution;
    public int AtlasHeightPerLevel => resolution;
    public int AtlasHeight => resolution * levels;

    // Radiance atlas: SxS tile per probe.
    public int RadianceAtlasWidth => (resolution * resolution) * worldProbeTileSize;
    public int RadianceAtlasHeight => (resolution * levels) * worldProbeTileSize;

    public DynamicTexture2D ProbeRadianceAtlas => radianceAtlas;
    public DynamicTexture2D ProbeVis0 => vis0;
    public DynamicTexture2D ProbeDist0 => dist0;
    public DynamicTexture2D ProbeMeta0 => meta0;
    public Texture2D ProbeDebugState0 => debugState0;

    public int ProbeRadianceAtlasTextureId => radianceAtlas.TextureId;
    public int ProbeVis0TextureId => vis0.TextureId;
    public int ProbeDist0TextureId => dist0.TextureId;
    public int ProbeMeta0TextureId => meta0.TextureId;
    public int ProbeDebugState0TextureId => debugState0.TextureId;

    /// <summary>Allocates and initializes probe atlases using the owned compute clear program.</summary>
    public LumOnWorldProbeClipmapGpuResources(ICoreAPI api, int resolution, int levels, int worldProbeTileSize)
    {
        if (resolution <= 0) throw new ArgumentOutOfRangeException(nameof(resolution));
        if (levels <= 0) throw new ArgumentOutOfRangeException(nameof(levels));
        if (worldProbeTileSize <= 0) throw new ArgumentOutOfRangeException(nameof(worldProbeTileSize));

        this.resolution = resolution;
        this.levels = levels;

        this.worldProbeTileSize = worldProbeTileSize;

        try
        {
            // Radiance atlas (SH replacement): one SxS octahedral tile per probe.
            radianceAtlas = owned.Own(DynamicTexture2D.Create(
            RadianceAtlasWidth,
            RadianceAtlasHeight,
            PixelInternalFormat.Rgba16f,
            TextureFilterMode.Nearest,
            "WorldProbe_ProbeRadianceAtlas"));

            // Visibility: RGBA16F (octU, octV, skyIntensity, aoConfidence)
            vis0 = owned.Own(DynamicTexture2D.Create(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, TextureFilterMode.Nearest, "WorldProbe_ProbeVis0"));

            // Distance: RG16F (meanLogDist, reserved)
            dist0 = owned.Own(DynamicTexture2D.Create(AtlasWidth, AtlasHeight, PixelInternalFormat.Rg16f, TextureFilterMode.Nearest, "WorldProbe_ProbeDist0"));

            // Meta: RG32F (confidence, uintBitsToFloat(flags))
            meta0 = owned.Own(DynamicTexture2D.Create(AtlasWidth, AtlasHeight, PixelInternalFormat.Rg32f, TextureFilterMode.Nearest, "WorldProbe_ProbeMeta0"));

            // Debug state: RGBA16 (UNorm) encoded by CPU from scheduler lifecycle.
            // R=stale, G=in-flight, B=valid, A=1.
            debugState0 = owned.Own(Texture2D.Create(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16, TextureFilterMode.Nearest, "WorldProbe_DebugState0"));

            // NOTE: Radiance atlas has different dimensions than per-probe scalar atlases, so it must live in its own FBO.
            radianceFbo = owned.Own(StaticGpuFramebuffer.Create([owned.Own(GpuFramebufferAttachment.FromTexture(radianceAtlas))],
            debugName: "WorldProbe_RadianceAtlasFbo"));

            // Per-probe scalar outputs.
            fbo = owned.Own(StaticGpuFramebuffer.Create([owned.Own(GpuFramebufferAttachment.FromTexture(vis0)),
            owned.Own(GpuFramebufferAttachment.FromTexture(dist0)), owned.Own(GpuFramebufferAttachment.FromTexture(meta0))],
            debugName: "WorldProbe_ClipmapFbo"));

            Label(radianceAtlas);
            Label(vis0);
            Label(dist0);
            Label(meta0);
            Label(debugState0);
            GlDebug.TryLabelFramebuffer(fbo.FboId, fbo.DebugName);
            GlDebug.TryLabelFramebuffer(radianceFbo.FboId, radianceFbo.DebugName);

            InitializeHistoryInvalidation(api);
            ClearAll();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Supplies the fixed three-output scalar atlas destination.</summary>
    public StaticGpuFramebuffer GetFbo() => fbo;

    /// <summary>Supplies the fixed radiance atlas destination independently of resident probe contents.</summary>
    public StaticGpuFramebuffer GetRadianceFbo() => radianceFbo;

    public void UploadDebugState0(ushort[] data)
    {
        debugState0.UploadData(data);
    }

    /// <summary>Releases atlas textures, framebuffers and the owned invalidation compute resources.</summary>
    public void Dispose()
    {
        // Every dependent owner must release even when another cleanup action fails.
        Rendering.Integration.RenderingCleanup.Run(
            () => historyClearSlots?.Dispose(), () => historyClear?.Dispose(), owned.Dispose);
    }

    private static void Label(GpuTexture texture)
    {
        if (texture.TextureId == 0) return;
        GlDebug.TryLabel(ObjectLabelIdentifier.Texture, texture.TextureId, texture.DebugName);
    }
}
