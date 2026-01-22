using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;

internal sealed class LumOnWorldProbeClipmapGpuResources : IDisposable
{
    private readonly int resolution;
    private readonly int levels;

    private readonly DynamicTexture2D sh0;
    private readonly DynamicTexture2D sh1;
    private readonly DynamicTexture2D sh2;
    private readonly DynamicTexture2D sky0;
    private readonly DynamicTexture2D vis0;
    private readonly DynamicTexture2D dist0;
    private readonly DynamicTexture2D meta0;

    private readonly Texture2D debugState0;

    private readonly GBuffer fbo;

    public int Resolution => resolution;
    public int Levels => levels;

    public int AtlasWidth => resolution * resolution;
    public int AtlasHeightPerLevel => resolution;
    public int AtlasHeight => resolution * levels;

    public int ProbeSh0TextureId => sh0.TextureId;
    public int ProbeSh1TextureId => sh1.TextureId;
    public int ProbeSh2TextureId => sh2.TextureId;
    public int ProbeSky0TextureId => sky0.TextureId;
    public int ProbeVis0TextureId => vis0.TextureId;
    public int ProbeDist0TextureId => dist0.TextureId;
    public int ProbeMeta0TextureId => meta0.TextureId;
    public int ProbeDebugState0TextureId => debugState0.TextureId;

    public LumOnWorldProbeClipmapGpuResources(int resolution, int levels)
    {
        if (resolution <= 0) throw new ArgumentOutOfRangeException(nameof(resolution));
        if (levels <= 0) throw new ArgumentOutOfRangeException(nameof(levels));

        this.resolution = resolution;
        this.levels = levels;

        // Per docs/LumOn.18-Probe-Data-Layout-and-Packing.md: L1 SH packed into 3 RGBA16F targets.
        // We pack all levels into a single 2D atlas per signal by stacking levels vertically:
        // v = y + level * resolution.
        sh0 = DynamicTexture2D.Create(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, TextureFilterMode.Nearest, "WorldProbe_ProbeSH0");
        sh1 = DynamicTexture2D.Create(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, TextureFilterMode.Nearest, "WorldProbe_ProbeSH1");
        sh2 = DynamicTexture2D.Create(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, TextureFilterMode.Nearest, "WorldProbe_ProbeSH2");

        // Sky lighting: scalar intensity projected into L1 SH (RGBA16F).
        sky0 = DynamicTexture2D.Create(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, TextureFilterMode.Nearest, "WorldProbe_ProbeSky0");

        // Visibility: RGBA16F (octU, octV, reserved, aoConfidence)
        vis0 = DynamicTexture2D.Create(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, TextureFilterMode.Nearest, "WorldProbe_ProbeVis0");

        // Distance: RG16F (meanLogDist, reserved)
        dist0 = DynamicTexture2D.Create(AtlasWidth, AtlasHeight, PixelInternalFormat.Rg16f, TextureFilterMode.Nearest, "WorldProbe_ProbeDist0");

        // Meta: RG32F (confidence, uintBitsToFloat(flags))
        meta0 = DynamicTexture2D.Create(AtlasWidth, AtlasHeight, PixelInternalFormat.Rg32f, TextureFilterMode.Nearest, "WorldProbe_ProbeMeta0");

        // Debug state: RGBA16 (UNorm) encoded by CPU from scheduler lifecycle.
        // R=stale, G=in-flight, B=valid, A=1.
        debugState0 = Texture2D.Create(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16, TextureFilterMode.Nearest, "WorldProbe_DebugState0");

        fbo = GBuffer.CreateMRT(
            "WorldProbe_ClipmapFbo",
            sh0,
            sh1,
            sh2,
            vis0,
            dist0,
            meta0,
            sky0) ?? throw new InvalidOperationException("Failed to create world-probe MRT FBO");

        Label(sh0);
        Label(sh1);
        Label(sh2);
        Label(sky0);
        Label(vis0);
        Label(dist0);
        Label(meta0);
        Label(debugState0);
        GlDebug.TryLabelFramebuffer(fbo.FboId, fbo.DebugName);

        ClearAll();
    }

    public GBuffer GetFbo() => fbo;

    public void UploadDebugState0(ushort[] data)
    {
        debugState0.UploadData(data);
    }

    public void ClearAll()
    {
        // Clear on create to keep deterministic sampling when uninitialized.
        fbo.Bind();
        GL.Viewport(0, 0, AtlasWidth, AtlasHeight);
        GL.ClearColor(0, 0, 0, 0);
        GL.Clear(ClearBufferMask.ColorBufferBit);
        GBuffer.Unbind();
    }

    public void Dispose()
    {
        fbo.Dispose();

        sh0.Dispose();
        sh1.Dispose();
        sh2.Dispose();
        sky0.Dispose();

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
