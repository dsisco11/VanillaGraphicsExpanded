using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;

internal sealed class LumOnWorldProbeClipmapGpuResources : IDisposable
{
    private readonly int resolution;
    private readonly int levels;
    private readonly int worldProbeTileSize;

    private readonly DynamicTexture2D radianceAtlas;
    private readonly DynamicTexture2D vis0;
    private readonly DynamicTexture2D dist0;
    private readonly DynamicTexture2D meta0;

    private readonly Texture2D debugState0;

    private readonly GpuFramebuffer fbo;
    private readonly GpuFramebuffer radianceFbo;

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

    // Legacy accessors (SH payload removed). These are temporary aliases to keep bindings and debug views compiling.
    public DynamicTexture2D ProbeSh0 => radianceAtlas;
    public DynamicTexture2D ProbeSh1 => radianceAtlas;
    public DynamicTexture2D ProbeSh2 => radianceAtlas;
    public DynamicTexture2D ProbeSky0 => radianceAtlas;
    public DynamicTexture2D ProbeVis0 => vis0;
    public DynamicTexture2D ProbeDist0 => dist0;
    public DynamicTexture2D ProbeMeta0 => meta0;
    public Texture2D ProbeDebugState0 => debugState0;

    public int ProbeRadianceAtlasTextureId => radianceAtlas.TextureId;

    public int ProbeSh0TextureId => radianceAtlas.TextureId;
    public int ProbeSh1TextureId => radianceAtlas.TextureId;
    public int ProbeSh2TextureId => radianceAtlas.TextureId;
    public int ProbeSky0TextureId => radianceAtlas.TextureId;
    public int ProbeVis0TextureId => vis0.TextureId;
    public int ProbeDist0TextureId => dist0.TextureId;
    public int ProbeMeta0TextureId => meta0.TextureId;
    public int ProbeDebugState0TextureId => debugState0.TextureId;

    public LumOnWorldProbeClipmapGpuResources(int resolution, int levels, int worldProbeTileSize)
    {
        if (resolution <= 0) throw new ArgumentOutOfRangeException(nameof(resolution));
        if (levels <= 0) throw new ArgumentOutOfRangeException(nameof(levels));
        if (worldProbeTileSize <= 0) throw new ArgumentOutOfRangeException(nameof(worldProbeTileSize));

        this.resolution = resolution;
        this.levels = levels;

        this.worldProbeTileSize = worldProbeTileSize;

        // Radiance atlas (SH replacement): one SxS octahedral tile per probe.
        radianceAtlas = DynamicTexture2D.Create(
            RadianceAtlasWidth,
            RadianceAtlasHeight,
            PixelInternalFormat.Rgba16f,
            TextureFilterMode.Nearest,
            "WorldProbe_ProbeRadianceAtlas");

        // Visibility: RGBA16F (octU, octV, skyIntensity, aoConfidence)
        vis0 = DynamicTexture2D.Create(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, TextureFilterMode.Nearest, "WorldProbe_ProbeVis0");

        // Distance: RG16F (meanLogDist, reserved)
        dist0 = DynamicTexture2D.Create(AtlasWidth, AtlasHeight, PixelInternalFormat.Rg16f, TextureFilterMode.Nearest, "WorldProbe_ProbeDist0");

        // Meta: RG32F (confidence, uintBitsToFloat(flags))
        meta0 = DynamicTexture2D.Create(AtlasWidth, AtlasHeight, PixelInternalFormat.Rg32f, TextureFilterMode.Nearest, "WorldProbe_ProbeMeta0");

        // Debug state: RGBA16 (UNorm) encoded by CPU from scheduler lifecycle.
        // R=stale, G=in-flight, B=valid, A=1.
        debugState0 = Texture2D.Create(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16, TextureFilterMode.Nearest, "WorldProbe_DebugState0");

        // NOTE: Radiance atlas has different dimensions than per-probe scalar atlases, so it must live in its own FBO.
        radianceFbo = GpuFramebuffer.CreateMRT(
            "WorldProbe_RadianceAtlasFbo",
            radianceAtlas) ?? throw new InvalidOperationException("Failed to create world-probe radiance atlas FBO");

        // Per-probe scalar outputs.
        fbo = GpuFramebuffer.CreateMRT(
            "WorldProbe_ClipmapFbo",
            vis0,
            dist0,
            meta0) ?? throw new InvalidOperationException("Failed to create world-probe MRT FBO");

        Label(radianceAtlas);
        Label(vis0);
        Label(dist0);
        Label(meta0);
        Label(debugState0);
        GlDebug.TryLabelFramebuffer(fbo.FboId, fbo.DebugName);
        GlDebug.TryLabelFramebuffer(radianceFbo.FboId, radianceFbo.DebugName);

        ClearAll();
    }

    public GpuFramebuffer GetFbo() => fbo;

    public GpuFramebuffer GetRadianceFbo() => radianceFbo;

    public void UploadDebugState0(ushort[] data)
    {
        debugState0.UploadData(data);
    }

    public void ClearAll()
    {
        // Clear on create to keep deterministic sampling when uninitialized.
        radianceFbo.Bind();
        GL.Viewport(0, 0, RadianceAtlasWidth, RadianceAtlasHeight);
        GL.ClearColor(0, 0, 0, 0);
        GL.Clear(ClearBufferMask.ColorBufferBit);

        fbo.Bind();
        GL.Viewport(0, 0, AtlasWidth, AtlasHeight);
        GL.ClearColor(0, 0, 0, 0);
        GL.Clear(ClearBufferMask.ColorBufferBit);

        GpuFramebuffer.Unbind();
    }

    public void Dispose()
    {
        radianceFbo.Dispose();
        fbo.Dispose();

        radianceAtlas.Dispose();

        vis0.Dispose();
        dist0.Dispose();
        meta0.Dispose();

        debugState0.Dispose();
    }

    private static void Label(GpuTexture texture)
    {
        if (texture.TextureId == 0) return;
        GlDebug.TryLabel(ObjectLabelIdentifier.Texture, texture.TextureId, texture.DebugName);
    }
}
