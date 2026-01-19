using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;

internal sealed class LumOnWorldProbeClipmapGpuResources : IDisposable
{
    private readonly int resolution;
    private readonly int levels;

    private readonly DynamicTexture[][] sh0;
    private readonly DynamicTexture[][] sh1;
    private readonly DynamicTexture[][] sh2;
    private readonly DynamicTexture[] vis0;
    private readonly DynamicTexture[] dist0;
    private readonly DynamicTexture[] meta0;

    private readonly GBuffer[] fbos;

    public int Resolution => resolution;
    public int Levels => levels;

    public int AtlasWidth => resolution * resolution;
    public int AtlasHeight => resolution;

    public LumOnWorldProbeClipmapGpuResources(int resolution, int levels)
    {
        if (resolution <= 0) throw new ArgumentOutOfRangeException(nameof(resolution));
        if (levels <= 0) throw new ArgumentOutOfRangeException(nameof(levels));

        this.resolution = resolution;
        this.levels = levels;

        sh0 = new DynamicTexture[levels][];
        sh1 = new DynamicTexture[levels][];
        sh2 = new DynamicTexture[levels][];

        vis0 = new DynamicTexture[levels];
        dist0 = new DynamicTexture[levels];
        meta0 = new DynamicTexture[levels];

        fbos = new GBuffer[levels];

        for (int level = 0; level < levels; level++)
        {
            // Per docs/LumOn.18-Probe-Data-Layout-and-Packing.md: L1 SH packed into 3 RGBA16F targets.
            var tSh0 = DynamicTexture.Create(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, TextureFilterMode.Nearest, $"WorldProbeL{level}_ProbeSH0");
            var tSh1 = DynamicTexture.Create(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, TextureFilterMode.Nearest, $"WorldProbeL{level}_ProbeSH1");
            var tSh2 = DynamicTexture.Create(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, TextureFilterMode.Nearest, $"WorldProbeL{level}_ProbeSH2");

            // Visibility: RGBA16F (octU, octV, reserved, aoConfidence)
            var tVis0 = DynamicTexture.Create(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, TextureFilterMode.Nearest, $"WorldProbeL{level}_ProbeVis0");

            // Distance: RG16F (meanLogDist, reserved)
            var tDist0 = DynamicTexture.Create(AtlasWidth, AtlasHeight, PixelInternalFormat.Rg16f, TextureFilterMode.Nearest, $"WorldProbeL{level}_ProbeDist0");

            // Meta: RG32F (confidence, uintBitsToFloat(flags))
            var tMeta0 = DynamicTexture.Create(AtlasWidth, AtlasHeight, PixelInternalFormat.Rg32f, TextureFilterMode.Nearest, $"WorldProbeL{level}_ProbeMeta0");

            sh0[level] = [tSh0];
            sh1[level] = [tSh1];
            sh2[level] = [tSh2];

            vis0[level] = tVis0;
            dist0[level] = tDist0;
            meta0[level] = tMeta0;

            fbos[level] = GBuffer.CreateMRT(
                $"WorldProbeL{level}_ClipmapFbo",
                tSh0,
                tSh1,
                tSh2,
                tVis0,
                tDist0,
                tMeta0) ?? throw new InvalidOperationException($"Failed to create world-probe MRT FBO for level {level}");

            // Clear on create to keep deterministic sampling when uninitialized.
            fbos[level].Bind();
            GL.Viewport(0, 0, AtlasWidth, AtlasHeight);
            GL.ClearColor(0, 0, 0, 0);
            GL.Clear(ClearBufferMask.ColorBufferBit);
            GBuffer.Unbind();
        }
    }

    public GBuffer GetFbo(int level) => fbos[level];

    public void Dispose()
    {
        for (int level = 0; level < levels; level++)
        {
            fbos[level]?.Dispose();

            sh0[level][0].Dispose();
            sh1[level][0].Dispose();
            sh2[level][0].Dispose();

            vis0[level].Dispose();
            dist0[level].Dispose();
            meta0[level].Dispose();
        }
    }
}
