using System;
using System.IO;
using System.Linq;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

[Collection("GPU")]
[Trait("Category", "GPU")]
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

            using var texL = DynamicTexture2D.Create(w, h, PixelInternalFormat.R32f, TextureFilterMode.Nearest);
            using var texTmp = DynamicTexture2D.Create(w, h, PixelInternalFormat.R32f, TextureFilterMode.Nearest);
            using var texBase = DynamicTexture2D.Create(w, h, PixelInternalFormat.R32f, TextureFilterMode.Nearest);
            using var texD0 = DynamicTexture2D.Create(w, h, PixelInternalFormat.R32f, TextureFilterMode.Nearest);
            using var texGxy = DynamicTexture2D.Create(w, h, PixelInternalFormat.Rg32f, TextureFilterMode.Nearest);
            using var texDiv = DynamicTexture2D.Create(w, h, PixelInternalFormat.R32f, TextureFilterMode.Nearest);
            using var texH = DynamicTexture2D.Create(w, h, PixelInternalFormat.R32f, TextureFilterMode.Nearest);
            using var texH2 = DynamicTexture2D.Create(w, h, PixelInternalFormat.R32f, TextureFilterMode.Nearest);
            using var texHn = DynamicTexture2D.Create(w, h, PixelInternalFormat.R32f, TextureFilterMode.Nearest);
            using var outAtlas = DynamicTexture2D.Create(w, h, PixelInternalFormat.Rgba16f, TextureFilterMode.Nearest);

            // 1) Luminance.
            RenderTo(texL, progL, () =>
            {
                atlas.Bind(0);
                GL.Uniform1(Uniform(progL, "u_atlas"), 0);
                GL.Uniform4(Uniform(progL, "u_atlasRectPx"), 0, 0, w, h);
                GL.Uniform2(Uniform(progL, "u_outSize"), w, h);
            });
            AssertFiniteAndHasVariance(texL, channels: 1, name: "L");

            // 2) base = Gauss(L, sigmaBig) with separable passes.
            RunGaussian1D(texL, texTmp, progG, sigma: cfg.SigmaBig, dirX: true);
            RunGaussian1D(texTmp, texBase, progG, sigma: cfg.SigmaBig, dirX: false);
            // With a large sigma (defaults: 16px), the blurred base can be almost constant for
            // small test textures. That's fine; just ensure it's finite.
            AssertFinite(texBase, channels: 1, name: "Base");

            // 3) D0 = L - base
            RenderTo(texD0, progSub, () =>
            {
                texL.Bind(0);
                texBase.Bind(1);
                GL.Uniform1(Uniform(progSub, "u_a"), 0);
                GL.Uniform1(Uniform(progSub, "u_b"), 1);
                GL.Uniform2(Uniform(progSub, "u_size"), w, h);
            });
            AssertFiniteAndHasVariance(texD0, channels: 1, name: "D0");

            // 4) Gradient field from D0 (we treat D0 as detail D).
            RenderTo(texGxy, progGrad, () =>
            {
                texD0.Bind(0);
                GL.Uniform1(Uniform(progGrad, "u_d"), 0);
                GL.Uniform2(Uniform(progGrad, "u_size"), w, h);
                GL.Uniform1(Uniform(progGrad, "u_gain"), cfg.Gain);
                GL.Uniform1(Uniform(progGrad, "u_maxSlope"), cfg.MaxSlope);
                GL.Uniform2(Uniform(progGrad, "u_edgeT"), cfg.EdgeT0, cfg.EdgeT1);
            });
            AssertFiniteAndHasVariance(texGxy, channels: 2, name: "Gxy");

            // 5) Divergence
            RenderTo(texDiv, progDiv, () =>
            {
                texGxy.Bind(0);
                GL.Uniform1(Uniform(progDiv, "u_g"), 0);
                GL.Uniform2(Uniform(progDiv, "u_size"), w, h);
            });
            AssertFiniteAndHasVariance(texDiv, channels: 1, name: "Div");

            // 6) Jacobi iterations (approximate periodic Poisson solve)
            ClearR32f(texH, 0f);
            for (int i = 0; i < 150; i++)
            {
                DynamicTexture2D src = (i % 2 == 0) ? texH : texH2;
                DynamicTexture2D dst = (i % 2 == 0) ? texH2 : texH;

                RenderTo(dst, progJacobi, () =>
                {
                    src.Bind(0);
                    texDiv.Bind(1);
                    GL.Uniform1(Uniform(progJacobi, "u_h"), 0);
                    GL.Uniform1(Uniform(progJacobi, "u_b"), 1);
                    GL.Uniform2(Uniform(progJacobi, "u_size"), w, h);
                });
            }
            // After odd iteration count, texH contains the last output.
            AssertFiniteAndHasVariance(texH, channels: 1, name: "H");

            // 7) Normalize (mean from CPU readback)
            float mean = MeanR32f(texH);
            (float invNeg, float invPos) = ComputeAsymmetricInvScales(texH, mean);
            RenderTo(texHn, progNorm, () =>
            {
                texH.Bind(0);
                GL.Uniform1(Uniform(progNorm, "u_h"), 0);
                GL.Uniform2(Uniform(progNorm, "u_size"), w, h);
                GL.Uniform1(Uniform(progNorm, "u_mean"), mean);
                GL.Uniform1(Uniform(progNorm, "u_invNeg"), invNeg);
                GL.Uniform1(Uniform(progNorm, "u_invPos"), invPos);
                GL.Uniform1(Uniform(progNorm, "u_heightStrength"), cfg.HeightStrength);
                GL.Uniform1(Uniform(progNorm, "u_gamma"), cfg.Gamma);
            });
            AssertFiniteAndHasVariance(texHn, channels: 1, name: "Hn");

            // 8) Pack to atlas
            RenderTo(outAtlas, progPack, () =>
            {
                texHn.Bind(0);
                GL.Uniform1(Uniform(progPack, "u_height"), 0);
                GL.Uniform2(Uniform(progPack, "u_solverSize"), w, h);
                GL.Uniform2(Uniform(progPack, "u_tileSize"), w, h);
                GL.Uniform2(Uniform(progPack, "u_viewportOrigin"), 0, 0);
                GL.Uniform1(Uniform(progPack, "u_normalStrength"), cfg.NormalStrength);
                GL.Uniform1(Uniform(progPack, "u_normalScale"), 1f);
                GL.Uniform1(Uniform(progPack, "u_depthScale"), 1f);
            });

            float[] packed = outAtlas.ReadPixels();
            (float minA, float maxA, int nanA, int ones) = AlphaStats(packed);

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
            using var texL = DynamicTexture2D.Create(w, h, PixelInternalFormat.R32f, TextureFilterMode.Nearest);
            using var texHn = DynamicTexture2D.Create(w, h, PixelInternalFormat.R32f, TextureFilterMode.Nearest);
            using var outAtlas = DynamicTexture2D.Create(w, h, PixelInternalFormat.Rgba16f, TextureFilterMode.Nearest);

            RenderTo(texL, progL, () =>
            {
                atlas.Bind(0);
                GL.Uniform1(Uniform(progL, "u_atlas"), 0);
                GL.Uniform4(Uniform(progL, "u_atlasRectPx"), 0, 0, w, h);
                GL.Uniform2(Uniform(progL, "u_outSize"), w, h);
            });

            // Treat luminance as the solved height field, just to stress normalize+pack.
            float mean = MeanR32f(texL);
            (float invNeg, float invPos) = ComputeAsymmetricInvScales(texL, mean);
            RenderTo(texHn, progNorm, () =>
            {
                texL.Bind(0);
                GL.Uniform1(Uniform(progNorm, "u_h"), 0);
                GL.Uniform2(Uniform(progNorm, "u_size"), w, h);
                GL.Uniform1(Uniform(progNorm, "u_mean"), mean);
                GL.Uniform1(Uniform(progNorm, "u_invNeg"), invNeg);
                GL.Uniform1(Uniform(progNorm, "u_invPos"), invPos);
                GL.Uniform1(Uniform(progNorm, "u_heightStrength"), cfg.HeightStrength);
                GL.Uniform1(Uniform(progNorm, "u_gamma"), cfg.Gamma);
            });

            RenderTo(outAtlas, progPack, () =>
            {
                texHn.Bind(0);
                GL.Uniform1(Uniform(progPack, "u_height"), 0);
                GL.Uniform2(Uniform(progPack, "u_solverSize"), w, h);
                GL.Uniform2(Uniform(progPack, "u_tileSize"), w, h);
                GL.Uniform2(Uniform(progPack, "u_viewportOrigin"), 0, 0);
                GL.Uniform1(Uniform(progPack, "u_normalStrength"), cfg.NormalStrength);
                GL.Uniform1(Uniform(progPack, "u_normalScale"), 1f);
                GL.Uniform1(Uniform(progPack, "u_depthScale"), 1f);
            });

            float[] packed = outAtlas.ReadPixels();
            (_, float maxA, _, int ones) = AlphaStats(packed);

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

            using var texL = DynamicTexture2D.Create(w, h, PixelInternalFormat.R32f, TextureFilterMode.Nearest);
            using var texTmp = DynamicTexture2D.Create(w, h, PixelInternalFormat.R32f, TextureFilterMode.Nearest);
            using var texBase = DynamicTexture2D.Create(w, h, PixelInternalFormat.R32f, TextureFilterMode.Nearest);
            using var texD0 = DynamicTexture2D.Create(w, h, PixelInternalFormat.R32f, TextureFilterMode.Nearest);
            using var texGxy = DynamicTexture2D.Create(w, h, PixelInternalFormat.Rg32f, TextureFilterMode.Nearest);
            using var texDiv = DynamicTexture2D.Create(w, h, PixelInternalFormat.R32f, TextureFilterMode.Nearest);
            using var texH = DynamicTexture2D.Create(w, h, PixelInternalFormat.R32f, TextureFilterMode.Nearest);
            using var texH2 = DynamicTexture2D.Create(w, h, PixelInternalFormat.R32f, TextureFilterMode.Nearest);
            using var texHn = DynamicTexture2D.Create(w, h, PixelInternalFormat.R32f, TextureFilterMode.Nearest);

            using var outAtlas1 = DynamicTexture2D.Create(w, h, PixelInternalFormat.Rgba16f, TextureFilterMode.Nearest);
            using var outAtlas2 = DynamicTexture2D.Create(w, h, PixelInternalFormat.Rgba16f, TextureFilterMode.Nearest);

            // 1) Luminance.
            RenderTo(texL, progL, () =>
            {
                atlas.Bind(0);
                GL.Uniform1(Uniform(progL, "u_atlas"), 0);
                GL.Uniform4(Uniform(progL, "u_atlasRectPx"), 0, 0, w, h);
                GL.Uniform2(Uniform(progL, "u_outSize"), w, h);
            });

            // 2) base = Gauss(L, sigmaBig)
            RunGaussian1D(texL, texTmp, progG, sigma: cfg.SigmaBig, dirX: true);
            RunGaussian1D(texTmp, texBase, progG, sigma: cfg.SigmaBig, dirX: false);

            // 3) D0 = L - base
            RenderTo(texD0, progSub, () =>
            {
                texL.Bind(0);
                texBase.Bind(1);
                GL.Uniform1(Uniform(progSub, "u_a"), 0);
                GL.Uniform1(Uniform(progSub, "u_b"), 1);
                GL.Uniform2(Uniform(progSub, "u_size"), w, h);
            });

            // 4) Gradient
            RenderTo(texGxy, progGrad, () =>
            {
                texD0.Bind(0);
                GL.Uniform1(Uniform(progGrad, "u_d"), 0);
                GL.Uniform2(Uniform(progGrad, "u_size"), w, h);
                GL.Uniform1(Uniform(progGrad, "u_gain"), cfg.Gain);
                GL.Uniform1(Uniform(progGrad, "u_maxSlope"), cfg.MaxSlope);
                GL.Uniform2(Uniform(progGrad, "u_edgeT"), cfg.EdgeT0, cfg.EdgeT1);
            });

            // 5) Divergence
            RenderTo(texDiv, progDiv, () =>
            {
                texGxy.Bind(0);
                GL.Uniform1(Uniform(progDiv, "u_g"), 0);
                GL.Uniform2(Uniform(progDiv, "u_size"), w, h);
            });

            // 6) Jacobi iterations
            ClearR32f(texH, 0f);
            for (int i = 0; i < 150; i++)
            {
                DynamicTexture2D src = (i % 2 == 0) ? texH : texH2;
                DynamicTexture2D dst = (i % 2 == 0) ? texH2 : texH;

                RenderTo(dst, progJacobi, () =>
                {
                    src.Bind(0);
                    texDiv.Bind(1);
                    GL.Uniform1(Uniform(progJacobi, "u_h"), 0);
                    GL.Uniform1(Uniform(progJacobi, "u_b"), 1);
                    GL.Uniform2(Uniform(progJacobi, "u_size"), w, h);
                });
            }

            // 7) Normalize
            float mean = MeanR32f(texH);
            (float invNeg, float invPos) = ComputeAsymmetricInvScales(texH, mean);
            RenderTo(texHn, progNorm, () =>
            {
                texH.Bind(0);
                GL.Uniform1(Uniform(progNorm, "u_h"), 0);
                GL.Uniform2(Uniform(progNorm, "u_size"), w, h);
                GL.Uniform1(Uniform(progNorm, "u_mean"), mean);
                GL.Uniform1(Uniform(progNorm, "u_invNeg"), invNeg);
                GL.Uniform1(Uniform(progNorm, "u_invPos"), invPos);
                GL.Uniform1(Uniform(progNorm, "u_heightStrength"), cfg.HeightStrength);
                GL.Uniform1(Uniform(progNorm, "u_gamma"), cfg.Gamma);
            });

            // 8) Pack twice with different scales.
            RenderTo(outAtlas1, progPack, () =>
            {
                texHn.Bind(0);
                GL.Uniform1(Uniform(progPack, "u_height"), 0);
                GL.Uniform2(Uniform(progPack, "u_solverSize"), w, h);
                GL.Uniform2(Uniform(progPack, "u_tileSize"), w, h);
                GL.Uniform2(Uniform(progPack, "u_viewportOrigin"), 0, 0);
                GL.Uniform1(Uniform(progPack, "u_normalStrength"), cfg.NormalStrength);
                GL.Uniform1(Uniform(progPack, "u_normalScale"), 1f);
                GL.Uniform1(Uniform(progPack, "u_depthScale"), 1f);
            });

            RenderTo(outAtlas2, progPack, () =>
            {
                texHn.Bind(0);
                GL.Uniform1(Uniform(progPack, "u_height"), 0);
                GL.Uniform2(Uniform(progPack, "u_solverSize"), w, h);
                GL.Uniform2(Uniform(progPack, "u_tileSize"), w, h);
                GL.Uniform2(Uniform(progPack, "u_viewportOrigin"), 0, 0);
                GL.Uniform1(Uniform(progPack, "u_normalStrength"), cfg.NormalStrength);
                GL.Uniform1(Uniform(progPack, "u_normalScale"), 0.5f);
                GL.Uniform1(Uniform(progPack, "u_depthScale"), 0.5f);
            });

            float[] packed1 = outAtlas1.ReadPixels();
            float[] packed2 = outAtlas2.ReadPixels();

            // Prove the final baked texture changes.
            float maxAbsDiffA = 0f;
            float maxAbsDiffRgb = 0f;
            for (int i = 0; i < packed1.Length; i += 4)
            {
                maxAbsDiffRgb = Math.Max(maxAbsDiffRgb, Math.Abs(packed1[i + 0] - packed2[i + 0]));
                maxAbsDiffRgb = Math.Max(maxAbsDiffRgb, Math.Abs(packed1[i + 1] - packed2[i + 1]));
                maxAbsDiffRgb = Math.Max(maxAbsDiffRgb, Math.Abs(packed1[i + 2] - packed2[i + 2]));
                maxAbsDiffA = Math.Max(maxAbsDiffA, Math.Abs(packed1[i + 3] - packed2[i + 3]));
            }

            Assert.True(maxAbsDiffA > 0.01f, $"Expected alpha to change with depthScale; maxAbsDiffA={maxAbsDiffA}");
            Assert.True(maxAbsDiffRgb > 0.001f, $"Expected RGB to change with normalScale; maxAbsDiffRgb={maxAbsDiffRgb}");
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

            using var texL = DynamicTexture2D.Create(w, h, PixelInternalFormat.R32f, TextureFilterMode.Nearest);
            using var texTmp = DynamicTexture2D.Create(w, h, PixelInternalFormat.R32f, TextureFilterMode.Nearest);
            using var texBase = DynamicTexture2D.Create(w, h, PixelInternalFormat.R32f, TextureFilterMode.Nearest);
            using var texD0 = DynamicTexture2D.Create(w, h, PixelInternalFormat.R32f, TextureFilterMode.Nearest);
            using var texGxy = DynamicTexture2D.Create(w, h, PixelInternalFormat.Rg32f, TextureFilterMode.Nearest);
            using var texDiv = DynamicTexture2D.Create(w, h, PixelInternalFormat.R32f, TextureFilterMode.Nearest);
            using var texH = DynamicTexture2D.Create(w, h, PixelInternalFormat.R32f, TextureFilterMode.Nearest);
            using var texH2 = DynamicTexture2D.Create(w, h, PixelInternalFormat.R32f, TextureFilterMode.Nearest);
            using var texHn = DynamicTexture2D.Create(w, h, PixelInternalFormat.R32f, TextureFilterMode.Nearest);
            using var outAtlasFlat = DynamicTexture2D.Create(w, h, PixelInternalFormat.Rgba16f, TextureFilterMode.Nearest);
            using var outAtlasScaled = DynamicTexture2D.Create(w, h, PixelInternalFormat.Rgba16f, TextureFilterMode.Nearest);

            // 1) Luminance.
            RenderTo(texL, progL, () =>
            {
                atlas.Bind(0);
                GL.Uniform1(Uniform(progL, "u_atlas"), 0);
                GL.Uniform4(Uniform(progL, "u_atlasRectPx"), 0, 0, w, h);
                GL.Uniform2(Uniform(progL, "u_outSize"), w, h);
            });

            // 2) base = Gauss(L, sigmaBig)
            RunGaussian1D(texL, texTmp, progG, sigma: cfg.SigmaBig, dirX: true);
            RunGaussian1D(texTmp, texBase, progG, sigma: cfg.SigmaBig, dirX: false);

            // 3) D0 = L - base
            RenderTo(texD0, progSub, () =>
            {
                texL.Bind(0);
                texBase.Bind(1);
                GL.Uniform1(Uniform(progSub, "u_a"), 0);
                GL.Uniform1(Uniform(progSub, "u_b"), 1);
                GL.Uniform2(Uniform(progSub, "u_size"), w, h);
            });

            // 4) Gradient
            RenderTo(texGxy, progGrad, () =>
            {
                texD0.Bind(0);
                GL.Uniform1(Uniform(progGrad, "u_d"), 0);
                GL.Uniform2(Uniform(progGrad, "u_size"), w, h);
                GL.Uniform1(Uniform(progGrad, "u_gain"), cfg.Gain);
                GL.Uniform1(Uniform(progGrad, "u_maxSlope"), cfg.MaxSlope);
                GL.Uniform2(Uniform(progGrad, "u_edgeT"), cfg.EdgeT0, cfg.EdgeT1);
            });

            // 5) Divergence
            RenderTo(texDiv, progDiv, () =>
            {
                texGxy.Bind(0);
                GL.Uniform1(Uniform(progDiv, "u_g"), 0);
                GL.Uniform2(Uniform(progDiv, "u_size"), w, h);
            });

            // 6) Jacobi iterations
            ClearR32f(texH, 0f);
            for (int i = 0; i < 150; i++)
            {
                DynamicTexture2D src = (i % 2 == 0) ? texH : texH2;
                DynamicTexture2D dst = (i % 2 == 0) ? texH2 : texH;

                RenderTo(dst, progJacobi, () =>
                {
                    src.Bind(0);
                    texDiv.Bind(1);
                    GL.Uniform1(Uniform(progJacobi, "u_h"), 0);
                    GL.Uniform1(Uniform(progJacobi, "u_b"), 1);
                    GL.Uniform2(Uniform(progJacobi, "u_size"), w, h);
                });
            }

            // 7) Normalize
            float mean = MeanR32f(texH);
            (float invNeg, float invPos) = ComputeAsymmetricInvScales(texH, mean);
            RenderTo(texHn, progNorm, () =>
            {
                texH.Bind(0);
                GL.Uniform1(Uniform(progNorm, "u_h"), 0);
                GL.Uniform2(Uniform(progNorm, "u_size"), w, h);
                GL.Uniform1(Uniform(progNorm, "u_mean"), mean);
                GL.Uniform1(Uniform(progNorm, "u_invNeg"), invNeg);
                GL.Uniform1(Uniform(progNorm, "u_invPos"), invPos);
                GL.Uniform1(Uniform(progNorm, "u_heightStrength"), cfg.HeightStrength);
                GL.Uniform1(Uniform(progNorm, "u_gamma"), cfg.Gamma);
            });

            // 8) Pack with normalScale=0 (flat) and normalScale=1 (non-flat).
            RenderTo(outAtlasFlat, progPack, () =>
            {
                texHn.Bind(0);
                GL.Uniform1(Uniform(progPack, "u_height"), 0);
                GL.Uniform2(Uniform(progPack, "u_solverSize"), w, h);
                GL.Uniform2(Uniform(progPack, "u_tileSize"), w, h);
                GL.Uniform2(Uniform(progPack, "u_viewportOrigin"), 0, 0);
                GL.Uniform1(Uniform(progPack, "u_normalStrength"), cfg.NormalStrength);
                GL.Uniform1(Uniform(progPack, "u_normalScale"), 0f);
                GL.Uniform1(Uniform(progPack, "u_depthScale"), 1f);
            });

            RenderTo(outAtlasScaled, progPack, () =>
            {
                texHn.Bind(0);
                GL.Uniform1(Uniform(progPack, "u_height"), 0);
                GL.Uniform2(Uniform(progPack, "u_solverSize"), w, h);
                GL.Uniform2(Uniform(progPack, "u_tileSize"), w, h);
                GL.Uniform2(Uniform(progPack, "u_viewportOrigin"), 0, 0);
                GL.Uniform1(Uniform(progPack, "u_normalStrength"), cfg.NormalStrength);
                GL.Uniform1(Uniform(progPack, "u_normalScale"), 1f);
                GL.Uniform1(Uniform(progPack, "u_depthScale"), 1f);
            });

            float[] flat = outAtlasFlat.ReadPixels();
            float[] scaled = outAtlasScaled.ReadPixels();

            // Flat normals should be constant-ish and near (0.5,0.5,1.0).
            float maxDevR = 0f, maxDevG = 0f, maxDevB = 0f;
            float maxDiffRgb = 0f;
            for (int i = 0; i < flat.Length; i += 4)
            {
                maxDevR = Math.Max(maxDevR, Math.Abs(flat[i + 0] - 0.5f));
                maxDevG = Math.Max(maxDevG, Math.Abs(flat[i + 1] - 0.5f));
                maxDevB = Math.Max(maxDevB, Math.Abs(flat[i + 2] - 1.0f));

                maxDiffRgb = Math.Max(maxDiffRgb, Math.Abs(flat[i + 0] - scaled[i + 0]));
                maxDiffRgb = Math.Max(maxDiffRgb, Math.Abs(flat[i + 1] - scaled[i + 1]));
                maxDiffRgb = Math.Max(maxDiffRgb, Math.Abs(flat[i + 2] - scaled[i + 2]));
            }

            Assert.True(maxDevR < 0.02f, $"Expected flat normal R≈0.5; maxDevR={maxDevR}");
            Assert.True(maxDevG < 0.02f, $"Expected flat normal G≈0.5; maxDevG={maxDevG}");
            Assert.True(maxDevB < 0.02f, $"Expected flat normal B≈1.0; maxDevB={maxDevB}");

            Assert.True(maxDiffRgb > 0.001f, $"Expected normalScale=0 vs 1 to change RGB; maxDiffRgb={maxDiffRgb}");
        }
    }

    private (ShaderTestHelper helper, string reason) TryCreateHelper()
    {
        string shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        string includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        if (!Directory.Exists(shaderPath) || !Directory.Exists(includePath))
        {
            return (null!, "Test shader assets not found in output directory.");
        }

        return (new ShaderTestHelper(shaderPath, includePath), string.Empty);
    }

    private static int Compile(ShaderTestHelper helper, string vsh, string fsh)
    {
        var res = helper.CompileAndLink(vsh, fsh);
        Assert.True(res.IsSuccess, res.ErrorMessage);
        return res.ProgramId;
    }

    private static int Uniform(int programId, string name)
    {
        int loc = GL.GetUniformLocation(programId, name);
        Assert.True(loc >= 0, $"Missing uniform '{name}'");
        return loc;
    }

    private void RenderTo(DynamicTexture2D dst, int programId, Action setup)
    {
        using var fbo = GpuFramebuffer.CreateSingle(dst, ownsTextures: false) ?? throw new InvalidOperationException("Failed to create FBO");

        fbo.Bind();
        GL.Viewport(0, 0, dst.Width, dst.Height);
        GL.Disable(EnableCap.Blend);

        GL.ClearColor(0f, 0f, 0f, 0f);
        GL.Clear(ClearBufferMask.ColorBufferBit);

        GL.UseProgram(programId);
        setup();

        // Draw fullscreen geometry. The bake vertex shader uses gl_VertexID, so the quad works fine.
        RenderFullscreenQuad();

        GL.UseProgram(0);
        GpuFramebuffer.Unbind();
    }

    private void RunGaussian1D(DynamicTexture2D src, DynamicTexture2D dst, int programId, float sigma, bool dirX)
    {
        int radius = (int)Math.Ceiling(3.0 * sigma);
        radius = Math.Clamp(radius, 0, 64);

        float[] weights = BuildGaussianWeights(sigma);

        RenderTo(dst, programId, () =>
        {
            src.Bind(0);
            GL.Uniform1(Uniform(programId, "u_src"), 0);
            GL.Uniform2(Uniform(programId, "u_size"), dst.Width, dst.Height);
            GL.Uniform2(Uniform(programId, "u_dir"), dirX ? 1 : 0, dirX ? 0 : 1);
            GL.Uniform1(Uniform(programId, "u_radius"), radius);

            int locWeights = Uniform(programId, "u_weights");
            GL.Uniform1(locWeights, 65, weights);
        });
    }

    private static float[] BuildGaussianWeights(float sigma)
    {
        // weights[0..64]
        float[] w = new float[65];

        if (sigma <= 0f)
        {
            w[0] = 1f;
            return w;
        }

        float twoSigma2 = 2f * sigma * sigma;

        double sum = 0;
        for (int i = 0; i <= 64; i++)
        {
            double x = i;
            double v = Math.Exp(-(x * x) / twoSigma2);
            w[i] = (float)v;
            sum += (i == 0) ? v : (2.0 * v);
        }

        float inv = sum > 0 ? (float)(1.0 / sum) : 1f;
        for (int i = 0; i <= 64; i++)
        {
            w[i] *= inv;
        }

        return w;
    }

    private void ClearR32f(DynamicTexture2D tex, float value)
    {
        using var fbo = GpuFramebuffer.CreateSingle(tex, ownsTextures: false) ?? throw new InvalidOperationException("Failed to create FBO");
        fbo.Bind();
        GL.Viewport(0, 0, tex.Width, tex.Height);
        GL.ClearColor(value, 0f, 0f, 0f);
        GL.Clear(ClearBufferMask.ColorBufferBit);
        GpuFramebuffer.Unbind();
    }

    private static float MeanR32f(DynamicTexture2D tex)
    {
        float[] data = tex.ReadPixels();
        if (data.Length == 0) return 0f;
        double sum = 0;
        for (int i = 0; i < data.Length; i++) sum += data[i];
        return (float)(sum / data.Length);
    }

    private static (float invNeg, float invPos) ComputeAsymmetricInvScales(DynamicTexture2D tex, float center)
    {
        float[] data = tex.ReadPixels();
        if (data.Length == 0) return (0f, 0f);

        float min = float.PositiveInfinity;
        float max = float.NegativeInfinity;
        for (int i = 0; i < data.Length; i++)
        {
            float v = data[i];
            if (v < min) min = v;
            if (v > max) max = v;
        }

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

        return DynamicTexture2D.CreateWithData(width, height, PixelInternalFormat.Rgba8, data, TextureFilterMode.Nearest);
    }

    private static void AssertFiniteAndHasVariance(DynamicTexture2D tex, int channels, string name)
    {
        float[] data = tex.ReadPixels();
        Assert.NotEmpty(data);

        double sum = 0;
        double sumSq = 0;
        int n = 0;

        int stride = channels;
        for (int i = 0; i < data.Length; i += stride)
        {
            // Use first channel only for variance checks.
            float v = data[i];
            Assert.False(float.IsNaN(v), $"{name} contains NaN");
            Assert.False(float.IsInfinity(v), $"{name} contains Infinity");

            sum += v;
            sumSq += v * v;
            n++;
        }

        double mean = sum / Math.Max(1, n);
        double var = sumSq / Math.Max(1, n) - mean * mean;
        if (var < 0) var = 0; // numeric noise
        Assert.True(var > 1e-8, $"{name} variance too low: {var}");
    }

    private static void AssertFinite(DynamicTexture2D tex, int channels, string name)
    {
        float[] data = tex.ReadPixels();
        Assert.NotEmpty(data);

        int stride = channels;
        for (int i = 0; i < data.Length; i += stride)
        {
            float v = data[i];
            Assert.False(float.IsNaN(v), $"{name} contains NaN");
            Assert.False(float.IsInfinity(v), $"{name} contains Infinity");
        }
    }

    private static (float minA, float maxA, int nanA, int ones) AlphaStats(float[] rgba)
    {
        float minA = float.PositiveInfinity;
        float maxA = float.NegativeInfinity;
        int nanA = 0;
        int ones = 0;

        for (int i = 0; i < rgba.Length; i += 4)
        {
            float a = rgba[i + 3];
            if (float.IsNaN(a) || float.IsInfinity(a))
            {
                nanA++;
                continue;
            }

            minA = Math.Min(minA, a);
            maxA = Math.Max(maxA, a);
            if (a > 0.999f) ones++;
        }

        return (minA, maxA, nanA, ones);
    }
}
