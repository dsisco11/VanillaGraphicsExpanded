using VanillaGraphicsExpanded.Tests.GPU.Assertions;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.Integration.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.IO;
using System.Linq;

using OpenTK.Graphics.OpenGL;

using HeightBakeParamsUboCpu = VanillaGraphicsExpanded.PBR.Materials.PbrHeightBakeParamsUbo;

using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class PbrHeightBakeFullChainTests : RenderTestBase
{
    private readonly HeadlessGLFixture fixture;

    public PbrHeightBakeFullChainTests(HeadlessGLFixture fixture) : base(fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public void FullChain_CheckerboardInput_AlphaIsFiniteAndNotAllSaturated()
    {
        EnsureContextValid();
        fixture.MakeCurrent();

        (ShaderTestHelper helper, string reason) = TryCreateHelper();
        if (helper is null)
        {
            Assert.SkipWhen(true, reason);
        }

        using (helper)
        {
            var cfg = new VgeConfig.NormalDepthBakeConfig();

            var progL = Compile(helper, "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_luminance.fsh");
            var progG = Compile(helper, "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_gauss1d.fsh");
            var progSub = Compile(helper, "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_sub.fsh");
            var progGrad = Compile(helper, "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_gradient.fsh");
            var progDiv = Compile(helper, "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_divergence.fsh");
            var progJacobi = Compile(helper, "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_jacobi.fsh");
            var progNorm = Compile(helper, "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_normalize.fsh");
            var progPack = Compile(helper, "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_pack_to_atlas.fsh");

            const int w = 64;
            const int h = 64;

            using var atlas = CreateCheckerboardAtlas(w, h, cell: 4);

            using var texL = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var texTmp = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var texBase = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var texD0 = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var texGxy = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rg32f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var texDiv = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var texH = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var texH2 = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var texHn = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var outAtlas = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();

            // 1) Luminance.
            RenderTo(texL, progL, (paramsUbo) =>
            {
                VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(0, atlas, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)atlas).Target)))]);

                UploadHeightBakeParams(paramsUbo, progL, (cpu) =>
                {
                    cpu.LuminanceAtlasRect = (x: 0, y: 0, w: w, h: h);
                    cpu.LuminanceDstSize = (width: w, height: h);
                });
            });
            AssertFiniteAndHasVariance(texL, channels: 1, name: "L");

            // 2) base = Gauss(L, sigmaBig) with separable passes.
            RunGaussian1D(texL, texTmp, progG, sigma: cfg.SigmaBig, dirX: true);
            RunGaussian1D(texTmp, texBase, progG, sigma: cfg.SigmaBig, dirX: false);
            // With a large sigma (defaults: 16px), the blurred base can be almost constant for
            // small test textures. That's fine; just ensure it's finite.
            AssertFinite(texBase, channels: 1, name: "Base");

            // 3) D0 = L - base
            RenderTo(texD0, progSub, (paramsUbo) =>
            {
                VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(0, texL, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)texL).Target)))]);
                VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(1, texBase, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)texBase).Target)))]);

                UploadHeightBakeParams(paramsUbo, progSub, (cpu) =>
                {
                    cpu.CommonSize = (width: w, height: h);
                    cpu.GaussianParams = (radius: 0, relContrast: 0);
                    cpu.SubParams = (eps: 1e-6f, vMax: 1e6f);
                });
            });
            AssertFiniteAndHasVariance(texD0, channels: 1, name: "D0");

            // 4) Gradient field from D0 (we treat D0 as detail D).
            RenderTo(texGxy, progGrad, (paramsUbo) =>
            {
                VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(0, texD0, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)texD0).Target)))]);

                UploadHeightBakeParams(paramsUbo, progGrad, (cpu) =>
                {
                    cpu.CommonSize = (width: w, height: h);
                    cpu.GradientParams = (gain: cfg.Gain, maxSlope: cfg.MaxSlope, edgeT0: cfg.EdgeT0, edgeT1: cfg.EdgeT1);
                });
            });
            AssertFiniteAndHasVariance(texGxy, channels: 2, name: "Gxy");

            // 5) Divergence
            RenderTo(texDiv, progDiv, (paramsUbo) =>
            {
                VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(0, texGxy, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)texGxy).Target)))]);

                UploadHeightBakeParams(paramsUbo, progDiv, (cpu) =>
                {
                    cpu.CommonSize = (width: w, height: h);
                });
            });
            AssertFiniteAndHasVariance(texDiv, channels: 1, name: "Div");

            // 6) Jacobi iterations (approximate periodic Poisson solve)
            ClearR32f(texH, 0f);
            for (int i = 0; i < 150; i++)
            {
                DynamicTexture2D src = (i % 2 == 0) ? texH : texH2;
                DynamicTexture2D dst = (i % 2 == 0) ? texH2 : texH;

                RenderTo(dst, progJacobi, (paramsUbo) =>
                {
                    VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(0, src, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)src).Target)))]);
                    VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(1, texDiv, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)texDiv).Target)))]);

                    UploadHeightBakeParams(paramsUbo, progJacobi, (cpu) =>
                    {
                        cpu.CommonSize = (width: w, height: h);
                    });
                });
            }
            // After odd iteration count, texH contains the last output.
            AssertFiniteAndHasVariance(texH, channels: 1, name: "H");

            // 7) Normalize (mean from CPU readback)
            float mean = MeanR32f(texH);
            (float invNeg, float invPos) = ComputeAsymmetricInvScales(texH, mean);
            RenderTo(texHn, progNorm, (paramsUbo) =>
            {
                VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(0, texH, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)texH).Target)))]);

                UploadHeightBakeParams(paramsUbo, progNorm, (cpu) =>
                {
                    cpu.CommonSize = (width: w, height: h);
                    cpu.NormalizeParams = (mean: mean, invNeg: invNeg, invPos: invPos, heightStrength: cfg.HeightStrength);
                    cpu.NormalizeGamma = cfg.Gamma;
                });
            });
            AssertFiniteAndHasVariance(texHn, channels: 1, name: "Hn");

            // 8) Pack to atlas
            RenderTo(outAtlas, progPack, (paramsUbo) =>
            {
                VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(0, texHn, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)texHn).Target)))]);

                VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(1, atlas, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)atlas).Target)))]);

                UploadHeightBakeParams(paramsUbo, progPack, (cpu) =>
                {
                    cpu.SolverSize = (width: w, height: h);
                    cpu.TileSize = (width: w, height: h);
                    cpu.ViewportOrigin = (x: 0, y: 0);
                    cpu.PackParams = (normalStrength: cfg.NormalStrength, normalScale: 1f, depthScale: 1f, eps: 0f);
                });
            });

            (float minA, float maxA, int nanA, int ones) = AlphaStats(outAtlas);

            Assert.Equal(0, nanA);
            Assert.True(maxA - minA > 0.05f, $"Packed alpha too flat. min={minA}, max={maxA}");
            Assert.True(ones < (w * h * 0.98f), $"Packed alpha appears saturated white. ones={ones}/{w*h}, min={minA}, max={maxA}");
        }
    }

    [Fact]
    public void FullChain_ExtremeHeightStrength_WillSaturateAlpha_ProvingTestDetectsIt()
    {
        EnsureContextValid();
        fixture.MakeCurrent();

        (ShaderTestHelper helper, string reason) = TryCreateHelper();
        if (helper is null)
        {
            Assert.SkipWhen(true, reason);
        }

        using (helper)
        {
            var cfg = new VgeConfig.NormalDepthBakeConfig
            {
                HeightStrength = 64f,
                Gamma = 1f,
                NormalStrength = 2f,
                SigmaBig = 8f,
                Gain = 2f,
                MaxSlope = 1f,
                EdgeT0 = 0.005f,
                EdgeT1 = 0.02f,
            };

            var progL = Compile(helper, "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_luminance.fsh");
            var progNorm = Compile(helper, "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_normalize.fsh");
            var progPack = Compile(helper, "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_pack_to_atlas.fsh");

            const int w = 32;
            const int h = 32;

            using var atlas = CreateCheckerboardAtlas(w, h, cell: 2);
            using var texL = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var texHn = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var outAtlas = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();

            RenderTo(texL, progL, (paramsUbo) =>
            {
                VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(0, atlas, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)atlas).Target)))]);

                UploadHeightBakeParams(paramsUbo, progL, (cpu) =>
                {
                    cpu.LuminanceAtlasRect = (x: 0, y: 0, w: w, h: h);
                    cpu.LuminanceDstSize = (width: w, height: h);
                });
            });

            // Treat luminance as the solved height field, just to stress normalize+pack.
            float mean = MeanR32f(texL);
            (float invNeg, float invPos) = ComputeAsymmetricInvScales(texL, mean);
            RenderTo(texHn, progNorm, (paramsUbo) =>
            {
                VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(0, texL, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)texL).Target)))]);

                UploadHeightBakeParams(paramsUbo, progNorm, (cpu) =>
                {
                    cpu.CommonSize = (width: w, height: h);
                    cpu.NormalizeParams = (mean: mean, invNeg: invNeg, invPos: invPos, heightStrength: cfg.HeightStrength);
                    cpu.NormalizeGamma = cfg.Gamma;
                });
            });

            RenderTo(outAtlas, progPack, (paramsUbo) =>
            {
                VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(0, texHn, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)texHn).Target)))]);

                VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(1, atlas, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)atlas).Target)))]);

                UploadHeightBakeParams(paramsUbo, progPack, (cpu) =>
                {
                    cpu.SolverSize = (width: w, height: h);
                    cpu.TileSize = (width: w, height: h);
                    cpu.ViewportOrigin = (x: 0, y: 0);
                    cpu.PackParams = (normalStrength: cfg.NormalStrength, normalScale: 1f, depthScale: 1f, eps: 0f);
                });
            });

            (_, float maxA, _, int ones) = AlphaStats(outAtlas);

            // This test is expected to show heavy saturation.
            Assert.True(maxA >= 0.99f);
            Assert.True(ones > (w * h * 0.25f), "Expected some alpha saturation at extreme height strength.");
        }
    }

    [Fact]
    public void FullChain_ScaleUniforms_ChangeFinalPackedOutput()
    {
        EnsureContextValid();
        fixture.MakeCurrent();

        (ShaderTestHelper helper, string reason) = TryCreateHelper();
        if (helper is null)
        {
            Assert.SkipWhen(true, reason);
        }

        using (helper)
        {
            var cfg = new VgeConfig.NormalDepthBakeConfig();

            var progL = Compile(helper, "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_luminance.fsh");
            var progG = Compile(helper, "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_gauss1d.fsh");
            var progSub = Compile(helper, "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_sub.fsh");
            var progGrad = Compile(helper, "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_gradient.fsh");
            var progDiv = Compile(helper, "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_divergence.fsh");
            var progJacobi = Compile(helper, "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_jacobi.fsh");
            var progNorm = Compile(helper, "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_normalize.fsh");
            var progPack = Compile(helper, "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_pack_to_atlas.fsh");

            const int w = 64;
            const int h = 64;

            using var atlas = CreateCheckerboardAtlas(w, h, cell: 4);

            using var texL = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var texTmp = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var texBase = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var texD0 = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var texGxy = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rg32f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var texDiv = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var texH = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var texH2 = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var texHn = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();

            using var outAtlas1 = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var outAtlas2 = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();

            // 1) Luminance.
            RenderTo(texL, progL, (paramsUbo) =>
            {
                VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(0, atlas, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)atlas).Target)))]);

                UploadHeightBakeParams(paramsUbo, progL, (cpu) =>
                {
                    cpu.LuminanceAtlasRect = (x: 0, y: 0, w: w, h: h);
                    cpu.LuminanceDstSize = (width: w, height: h);
                });
            });

            // 2) base = Gauss(L, sigmaBig)
            RunGaussian1D(texL, texTmp, progG, sigma: cfg.SigmaBig, dirX: true);
            RunGaussian1D(texTmp, texBase, progG, sigma: cfg.SigmaBig, dirX: false);

            // 3) D0 = L - base
            RenderTo(texD0, progSub, (paramsUbo) =>
            {
                VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(0, texL, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)texL).Target)))]);
                VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(1, texBase, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)texBase).Target)))]);

                UploadHeightBakeParams(paramsUbo, progSub, (cpu) =>
                {
                    cpu.CommonSize = (width: w, height: h);
                    cpu.GaussianParams = (radius: 0, relContrast: 0);
                    cpu.SubParams = (eps: 1e-6f, vMax: 1e6f);
                });
            });

            // 4) Gradient
            RenderTo(texGxy, progGrad, (paramsUbo) =>
            {
                VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(0, texD0, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)texD0).Target)))]);

                UploadHeightBakeParams(paramsUbo, progGrad, (cpu) =>
                {
                    cpu.CommonSize = (width: w, height: h);
                    cpu.GradientParams = (gain: cfg.Gain, maxSlope: cfg.MaxSlope, edgeT0: cfg.EdgeT0, edgeT1: cfg.EdgeT1);
                });
            });

            // 5) Divergence
            RenderTo(texDiv, progDiv, (paramsUbo) =>
            {
                VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(0, texGxy, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)texGxy).Target)))]);

                UploadHeightBakeParams(paramsUbo, progDiv, (cpu) =>
                {
                    cpu.CommonSize = (width: w, height: h);
                });
            });

            // 6) Jacobi iterations
            ClearR32f(texH, 0f);
            for (int i = 0; i < 150; i++)
            {
                DynamicTexture2D src = (i % 2 == 0) ? texH : texH2;
                DynamicTexture2D dst = (i % 2 == 0) ? texH2 : texH;

                RenderTo(dst, progJacobi, (paramsUbo) =>
                {
                    VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(0, src, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)src).Target)))]);
                    VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(1, texDiv, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)texDiv).Target)))]);

                    UploadHeightBakeParams(paramsUbo, progJacobi, (cpu) =>
                    {
                        cpu.CommonSize = (width: w, height: h);
                    });
                });
            }

            // 7) Normalize
            float mean = MeanR32f(texH);
            (float invNeg, float invPos) = ComputeAsymmetricInvScales(texH, mean);
            RenderTo(texHn, progNorm, (paramsUbo) =>
            {
                VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(0, texH, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)texH).Target)))]);

                UploadHeightBakeParams(paramsUbo, progNorm, (cpu) =>
                {
                    cpu.CommonSize = (width: w, height: h);
                    cpu.NormalizeParams = (mean: mean, invNeg: invNeg, invPos: invPos, heightStrength: cfg.HeightStrength);
                    cpu.NormalizeGamma = cfg.Gamma;
                });
            });

            // 8) Pack twice with different scales.
            RenderTo(outAtlas1, progPack, (paramsUbo) =>
            {
                VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(0, texHn, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)texHn).Target)))]);

                VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(1, atlas, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)atlas).Target)))]);

                UploadHeightBakeParams(paramsUbo, progPack, (cpu) =>
                {
                    cpu.SolverSize = (width: w, height: h);
                    cpu.TileSize = (width: w, height: h);
                    cpu.ViewportOrigin = (x: 0, y: 0);
                    cpu.PackParams = (normalStrength: cfg.NormalStrength, normalScale: 1f, depthScale: 1f, eps: 0f);
                });
            });

            RenderTo(outAtlas2, progPack, (paramsUbo) =>
            {
                VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(0, texHn, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)texHn).Target)))]);

                VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(1, atlas, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)atlas).Target)))]);

                UploadHeightBakeParams(paramsUbo, progPack, (cpu) =>
                {
                    cpu.SolverSize = (width: w, height: h);
                    cpu.TileSize = (width: w, height: h);
                    cpu.ViewportOrigin = (x: 0, y: 0);
                    cpu.PackParams = (normalStrength: cfg.NormalStrength, normalScale: 0.5f, depthScale: 0.5f, eps: 0f);
                });
            });

            // The original maxima exceed their thresholds exactly when at least one corresponding component does.
            Assert.True(ImageAssertions.Predicate(outAtlas1, GpuAssertionPredicate.PbrHeightBakeFullChainTests_FullChain_ScaleUniforms_ChangeFinalPackedOutput,
                outAtlas2).MatchingTexels>0,"Expected alpha to change with depthScale.");
            Assert.True(ImageAssertions.Predicate(outAtlas1, GpuAssertionPredicate.PbrHeightBakeFullChainTests_FullChain_ScaleUniforms_ChangeFinalPackedOutput_2,
                outAtlas2).MatchingTexels>0,"Expected RGB to change with normalScale.");
        }
    }

    [Fact]
    public void FullChain_NormalScaleZero_FlattensPackedNormalsRgb()
    {
        EnsureContextValid();
        fixture.MakeCurrent();

        (ShaderTestHelper helper, string reason) = TryCreateHelper();
        if (helper is null)
        {
            Assert.SkipWhen(true, reason);
        }

        using (helper)
        {
            var cfg = new VgeConfig.NormalDepthBakeConfig
            {
                // Ensure normals are actually generated (otherwise normalScale doesn't matter).
                NormalStrength = 4f
            };

            var progL = Compile(helper, "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_luminance.fsh");
            var progG = Compile(helper, "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_gauss1d.fsh");
            var progSub = Compile(helper, "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_sub.fsh");
            var progGrad = Compile(helper, "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_gradient.fsh");
            var progDiv = Compile(helper, "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_divergence.fsh");
            var progJacobi = Compile(helper, "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_jacobi.fsh");
            var progNorm = Compile(helper, "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_normalize.fsh");
            var progPack = Compile(helper, "pbr_heightbake_fullscreen.vsh", "pbr_heightbake_pack_to_atlas.fsh");

            const int w = 64;
            const int h = 64;

            using var atlas = CreateCheckerboardAtlas(w, h, cell: 4);

            using var texL = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var texTmp = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var texBase = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var texD0 = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var texGxy = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rg32f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var texDiv = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var texH = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var texH2 = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var texHn = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var outAtlasFlat = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var outAtlasScaled = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();

            // 1) Luminance.
            RenderTo(texL, progL, (paramsUbo) =>
            {
                VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(0, atlas, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)atlas).Target)))]);

                UploadHeightBakeParams(paramsUbo, progL, (cpu) =>
                {
                    cpu.LuminanceAtlasRect = (x: 0, y: 0, w: w, h: h);
                    cpu.LuminanceDstSize = (width: w, height: h);
                });
            });

            // 2) base = Gauss(L, sigmaBig)
            RunGaussian1D(texL, texTmp, progG, sigma: cfg.SigmaBig, dirX: true);
            RunGaussian1D(texTmp, texBase, progG, sigma: cfg.SigmaBig, dirX: false);

            // 3) D0 = L - base
            RenderTo(texD0, progSub, (paramsUbo) =>
            {
                VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(0, texL, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)texL).Target)))]);
                VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(1, texBase, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)texBase).Target)))]);

                UploadHeightBakeParams(paramsUbo, progSub, (cpu) =>
                {
                    cpu.CommonSize = (width: w, height: h);
                    cpu.GaussianParams = (radius: 0, relContrast: 0);
                    cpu.SubParams = (eps: 1e-6f, vMax: 1e6f);
                });
            });

            // 4) Gradient
            RenderTo(texGxy, progGrad, (paramsUbo) =>
            {
                VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(0, texD0, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)texD0).Target)))]);

                UploadHeightBakeParams(paramsUbo, progGrad, (cpu) =>
                {
                    cpu.CommonSize = (width: w, height: h);
                    cpu.GradientParams = (gain: cfg.Gain, maxSlope: cfg.MaxSlope, edgeT0: cfg.EdgeT0, edgeT1: cfg.EdgeT1);
                });
            });

            // 5) Divergence
            RenderTo(texDiv, progDiv, (paramsUbo) =>
            {
                VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(0, texGxy, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)texGxy).Target)))]);

                UploadHeightBakeParams(paramsUbo, progDiv, (cpu) =>
                {
                    cpu.CommonSize = (width: w, height: h);
                });
            });

            // 6) Jacobi iterations
            ClearR32f(texH, 0f);
            for (int i = 0; i < 150; i++)
            {
                DynamicTexture2D src = (i % 2 == 0) ? texH : texH2;
                DynamicTexture2D dst = (i % 2 == 0) ? texH2 : texH;

                RenderTo(dst, progJacobi, (paramsUbo) =>
                {
                    VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(0, src, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)src).Target)))]);
                    VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(1, texDiv, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)texDiv).Target)))]);

                    UploadHeightBakeParams(paramsUbo, progJacobi, (cpu) =>
                    {
                        cpu.CommonSize = (width: w, height: h);
                    });
                });
            }

            // 7) Normalize
            float mean = MeanR32f(texH);
            (float invNeg, float invPos) = ComputeAsymmetricInvScales(texH, mean);
            RenderTo(texHn, progNorm, (paramsUbo) =>
            {
                VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(0, texH, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)texH).Target)))]);

                UploadHeightBakeParams(paramsUbo, progNorm, (cpu) =>
                {
                    cpu.CommonSize = (width: w, height: h);
                    cpu.NormalizeParams = (mean: mean, invNeg: invNeg, invPos: invPos, heightStrength: cfg.HeightStrength);
                    cpu.NormalizeGamma = cfg.Gamma;
                });
            });

            // 8) Pack with normalScale=0 (flat) and normalScale=1 (non-flat).
            RenderTo(outAtlasFlat, progPack, (paramsUbo) =>
            {
                VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(0, texHn, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)texHn).Target)))]);

                VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(1, atlas, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)atlas).Target)))]);

                UploadHeightBakeParams(paramsUbo, progPack, (cpu) =>
                {
                    cpu.SolverSize = (width: w, height: h);
                    cpu.TileSize = (width: w, height: h);
                    cpu.ViewportOrigin = (x: 0, y: 0);
                    cpu.PackParams = (normalStrength: cfg.NormalStrength, normalScale: 0f, depthScale: 1f, eps: 0f);
                });
            });

            RenderTo(outAtlasScaled, progPack, (paramsUbo) =>
            {
                VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(0, texHn, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)texHn).Target)))]);

                VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(1, atlas, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)atlas).Target)))]);

                UploadHeightBakeParams(paramsUbo, progPack, (cpu) =>
                {
                    cpu.SolverSize = (width: w, height: h);
                    cpu.TileSize = (width: w, height: h);
                    cpu.ViewportOrigin = (x: 0, y: 0);
                    cpu.PackParams = (normalStrength: cfg.NormalStrength, normalScale: 1f, depthScale: 1f, eps: 0f);
                });
            });

            ImageAssertions.Predicate(outAtlasFlat, GpuAssertionPredicate.PbrHeightBakeFullChainTests_FullChain_NormalScaleZero_FlattensPackedNormalsRgb).AssertEqual();
            Assert.True(ImageAssertions.Predicate(outAtlasFlat, GpuAssertionPredicate.PbrHeightBakeFullChainTests_FullChain_ScaleUniforms_ChangeFinalPackedOutput_2,
                outAtlasScaled).MatchingTexels>0,"Expected normalScale=0 vs 1 to change RGB.");
        }
    }

    private (ShaderTestHelper helper, string reason) TryCreateHelper()
    {
        string shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        string includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        if (!TestAssetSnapshot.HasDirectory(shaderPath) || !TestAssetSnapshot.HasDirectory(includePath))
        {
            return (null!, "Test shader assets not found in output directory.");
        }

        return (new ShaderTestHelper(), string.Empty);
    }

    private static int Compile(ShaderTestHelper helper, string vsh, string fsh)
    {
        var res = helper.CompileAndLink(vsh, fsh);
        Assert.True(res.IsSuccess, res.ErrorMessage);
        return res.ProgramId;
    }


    private static void UploadHeightBakeParams(ObjectParamsUbo paramsUbo, int programId, Action<HeightBakeParamsUboCpu> configure)
    {
        PbrHeightBakeParamsUbo.EnsureHeightBakeBlockBound(programId);

        var cpu = new HeightBakeParamsUboCpu();
        configure(cpu);

        paramsUbo.UploadAndBind(cpu.Bytes);
    }

    private void RenderTo(DynamicTexture2D dst, int programId, Action<ObjectParamsUbo> setup)
    {
        using var fbo = DynamicGpuFramebuffer.CreateSingle(dst) ?? throw new InvalidOperationException("Failed to create FBO");
        using var objectParamsUbo = new ObjectParamsUbo($"Tests.Pbr.HeightBake.ParamsUBO.{programId}");

        fbo.Bind();
        GL.Viewport(0, 0, dst.Width, dst.Height);
        GL.Disable(EnableCap.Blend);

        GL.ClearColor(0f, 0f, 0f, 0f);
        GL.Clear(ClearBufferMask.ColorBufferBit);

        GL.UseProgram(programId);
        setup(objectParamsUbo);

        // Draw fullscreen geometry. The bake vertex shader uses gl_VertexID, so the quad works fine.
        RenderFullscreenQuad();

        GL.UseProgram(0);
        GpuFramebuffer.Unbind();
    }

    /// <summary>Executes one Gaussian axis using the production block's 64-weight capacity.</summary>
    private void RunGaussian1D(DynamicTexture2D src, DynamicTexture2D dst, int programId, float sigma, bool dirX)
    {
        int radius = (int)Math.Ceiling(3.0 * sigma);
        radius = Math.Clamp(radius, 0, 63);

        float[] weights = BuildGaussianWeights(sigma);

        RenderTo(dst, programId, (paramsUbo) =>
        {
            VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(0, src, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)src).Target)))]);

            UploadHeightBakeParams(paramsUbo, programId, (cpu) =>
            {
                cpu.CommonSize = (dst.Width, dst.Height);
                cpu.GaussianDirection = (dirX ? 1 : 0, dirX ? 0 : 1);
                cpu.GaussianParams = (radius: radius, relContrast: 0);
                cpu.SetKernelWeights(weights, count: 64);
            });
        });
    }

    /// <summary>Normalizes the symmetric kernel within the production uniform block's fixed capacity.</summary>
    private static float[] BuildGaussianWeights(float sigma)
    {
        // The production block holds weights[0..63].
        float[] w = new float[64];

        if (sigma <= 0f)
        {
            w[0] = 1f;
            return w;
        }

        float twoSigma2 = 2f * sigma * sigma;

        double sum = 0;
        for (int i = 0; i <= 63; i++)
        {
            double x = i;
            double v = Math.Exp(-(x * x) / twoSigma2);
            w[i] = (float)v;
            sum += (i == 0) ? v : (2.0 * v);
        }

        float inv = sum > 0 ? (float)(1.0 / sum) : 1f;
        for (int i = 0; i <= 63; i++)
        {
            w[i] *= inv;
        }

        return w;
    }

    private void ClearR32f(DynamicTexture2D tex, float value)
    {
        using var fbo = DynamicGpuFramebuffer.CreateSingle(tex) ?? throw new InvalidOperationException("Failed to create FBO");
        fbo.Bind();
        GL.Viewport(0, 0, tex.Width, tex.Height);
        GL.ClearColor(value, 0f, 0f, 0f);
        GL.Clear(ClearBufferMask.ColorBufferBit);
        GpuFramebuffer.Unbind();
    }

    /// <summary>Returns the mean of every original scalar through a bounded double GPU sum.</summary>
    private float MeanR32f(DynamicTexture2D tex)
    {
        return (float)(ImageAssertions.Statistics(tex,channelMask:1).Sum(0)/(tex.Width*tex.Height));
    }

    /// <summary>Derives authored normalization inputs from complete-domain extrema without downloading the height field.</summary>
    private (float invNeg, float invPos) ComputeAsymmetricInvScales(DynamicTexture2D tex, float center)
    {
        var extrema=ImageAssertions.Inspect(tex,channelMask:1);
        float min=(float)extrema.Minimum,max=(float)extrema.Maximum;

        const float eps = 1e-6f;
        const float maxInv = 64f;
        float negSpan = center - min;
        float posSpan = max - center;
        float invNeg = negSpan > eps ? Math.Min(1f / negSpan, maxInv) : 0f;
        float invPos = posSpan > eps ? Math.Min(1f / posSpan, maxInv) : 0f;
        return (invNeg, invPos);
    }

    private static DynamicTexture2D CreateCheckerboardAtlas(int width, int height, int cell)
    {
        float[] data = new float[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int cx = x / cell;
                int cy = y / cell;
                bool on = ((cx + cy) & 1) == 0;
                float v = on ? 1.0f : 0.0f;

                int idx = (y * width + x) * 4;
                data[idx + 0] = v;
                data[idx + 1] = v;
                data[idx + 2] = v;
                data[idx + 3] = 1.0f;
            }
        }

        return RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = width, Height = height, Format = TextureFormat.Rgba8, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, width, height), new RHIPixelEncoding(TextureFormatHelper.GetPixelFormat(TextureFormat.Rgba8), PixelComponentType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((data).AsSpan()).ToArray())]).RequireResource();
    }

    /// <summary>Checks the original first-channel finite values and variance, retaining float products with double sums.</summary>
    private void AssertFiniteAndHasVariance(DynamicTexture2D tex, int channels, string name)
    {
        var moments=ImageAssertions.Statistics(tex,channelMask:1,squareInFloat:true);
        Assert.True(moments.NonFinite==0,name+": "+moments);
        int count=tex.Width*tex.Height;
        double mean=moments.Sum(0)/count;
        double variance=Math.Max(0,moments.SumSquares(0)/count-mean*mean);
        Assert.True(variance>1e-8,$"{name} variance too low: {variance}");
    }

    /// <summary>Checks the original first-channel finite contract over the complete image.</summary>
    private void AssertFinite(DynamicTexture2D tex, int channels, string name)
    {
        var observation=ImageAssertions.Inspect(tex,channelMask:1);
        Assert.True(observation.NonFinite==0,name+": "+observation);
    }

    /// <summary>Retains every alpha value's finite classification, extrema and strict white-saturation threshold.</summary>
    private (float minA, float maxA, int nanA, int ones) AlphaStats(GpuTexture image)
    {
        var observation=ImageAssertions.Inspect(image,channelMask:8);
        uint ones=ImageAssertions.Predicate(image, GpuAssertionPredicate.PbrHeightBakeFullChainTests_AlphaStats).MatchingTexels;
        return ((float)observation.Minimum,(float)observation.Maximum,(int)observation.NonFinite,(int)ones);
    }
}
