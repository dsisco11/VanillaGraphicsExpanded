using VanillaGraphicsExpanded.Tests.GPU.Assertions;
using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.PBR.Materials;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises the production height-bake chain while preserving end-to-end image guarantees.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class PbrHeightBakeFullChainTests : RenderTestBase
{


    private readonly HeadlessGLFixture fixture;

    #region Public API
    /// <summary>Creates this integration suite over the shared headless OpenGL fixture.</summary>
    public PbrHeightBakeFullChainTests(HeadlessGLFixture fixture) : base(fixture)
    {
        this.fixture = fixture;
    }

    /// <summary>Checks that checkerboard luminance survives the full production bake chain with usable alpha.</summary>
    [Fact]
    public void FullChain_CheckerboardInput_AlphaIsFiniteAndNotAllSaturated()
    {
        EnsureContextValid();
        fixture.MakeCurrent();

        // Arrange: create the original checkerboard atlas and bake intermediates.
        PbrHeightBakeTestPrograms.EnsureAssetsAvailable();
        using var programs = new PbrHeightBakeTestPrograms();
        {
            var cfg = new VgeConfig.NormalDepthBakeConfig();

            var progL = programs.Create("pbr_heightbake_luminance");
            var progG = programs.Create("pbr_heightbake_gauss1d");
            var progSub = programs.Create("pbr_heightbake_sub");
            var progGrad = programs.Create("pbr_heightbake_gradient");
            var progDiv = programs.Create("pbr_heightbake_divergence");
            var progJacobi = programs.Create("pbr_heightbake_jacobi");
            var progNorm = programs.Create("pbr_heightbake_normalize");
            var progPack = programs.Create("pbr_heightbake_pack_to_atlas");

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

            // Act: run the complete production bake sequence.
            // 1) Luminance.
            RenderTo(programs, texL, progL, (parameters) => {
                progL.atlas = atlas;


                    parameters.LuminanceAtlasRect = (x: 0, y: 0, w: w, h: h);
                    parameters.LuminanceDstSize = (width: w, height: h);
            });
            AssertFiniteAndHasVariance(texL, channels: 1, name: "L");

            // 2) base = Gauss(L, sigmaBig) with separable passes.
            RunGaussian1D(programs, texL, texTmp, progG, sigma: cfg.SigmaBig, dirX: true);
            RunGaussian1D(programs, texTmp, texBase, progG, sigma: cfg.SigmaBig, dirX: false);
            // With a large sigma (defaults: 16px), the blurred base can be almost constant for
            // small test textures. That's fine; just ensure it's finite.
            AssertFinite(texBase, channels: 1, name: "Base");

            // 3) D0 = L - base
            RenderTo(programs, texD0, progSub, (parameters) => {
                progSub.a = texL;
                progSub.b = texBase;


                    parameters.CommonSize = (width: w, height: h);
                    parameters.GaussianParams = (radius: 0, relContrast: 0);
                    parameters.SubParams = (eps: 1e-6f, vMax: 1e6f);
            });
            AssertFiniteAndHasVariance(texD0, channels: 1, name: "D0");

            // 4) Gradient field from D0 (we treat D0 as detail D).
            RenderTo(programs, texGxy, progGrad, (parameters) => {
                progGrad.d = texD0;


                    parameters.CommonSize = (width: w, height: h);
                    parameters.GradientParams = (gain: cfg.Gain, maxSlope: cfg.MaxSlope, edgeT0: cfg.EdgeT0, edgeT1: cfg.EdgeT1);
            });
            AssertFiniteAndHasVariance(texGxy, channels: 2, name: "Gxy");

            // 5) Divergence
            RenderTo(programs, texDiv, progDiv, (parameters) => {
                progDiv.g = texGxy;


                    parameters.CommonSize = (width: w, height: h);
            });
            AssertFiniteAndHasVariance(texDiv, channels: 1, name: "Div");

            // 6) Jacobi iterations (approximate periodic Poisson solve)
            ClearR32f(texH, 0f);
            for (int i = 0; i < 150; i++)
            {
                DynamicTexture2D src = (i % 2 == 0) ? texH : texH2;
                DynamicTexture2D dst = (i % 2 == 0) ? texH2 : texH;

                RenderTo(programs, dst, progJacobi, (parameters) => {
                    progJacobi.h = src;
                    progJacobi.b = texDiv;


                        parameters.CommonSize = (width: w, height: h);
                });
            }
            // After odd iteration count, texH contains the last output.
            AssertFiniteAndHasVariance(texH, channels: 1, name: "H");

            // 7) Normalize (mean from CPU readback)
            float mean = MeanR32f(texH);
            (float invNeg, float invPos) = ComputeAsymmetricInvScales(texH, mean);
            RenderTo(programs, texHn, progNorm, (parameters) => {
                progNorm.h = texH;


                    parameters.CommonSize = (width: w, height: h);
                    parameters.NormalizeParams = (mean: mean, invNeg: invNeg, invPos: invPos, heightStrength: cfg.HeightStrength);
                    parameters.NormalizeGamma = cfg.Gamma;
            });
            AssertFiniteAndHasVariance(texHn, channels: 1, name: "Hn");

            // 8) Pack to atlas
            RenderTo(programs, outAtlas, progPack, (parameters) => {
                progPack.height = texHn;

                progPack.albedoAtlas = atlas;


                    parameters.SolverSize = (width: w, height: h);
                    parameters.TileSize = (width: w, height: h);
                    parameters.ViewportOrigin = (x: 0, y: 0);
                    parameters.PackParams = (normalStrength: cfg.NormalStrength, normalScale: 1f, depthScale: 1f, eps: 0f);
            });

            // Assert: preserve the packed alpha finiteness, variation, and saturation limits.
            (float minA, float maxA, int nanA, int ones) = AlphaStats(outAtlas);

            Assert.Equal(0, nanA);
            Assert.True(maxA - minA > 0.05f, $"Packed alpha too flat. min={minA}, max={maxA}");
            Assert.True(ones < (w * h * 0.98f), $"Packed alpha appears saturated white. ones={ones}/{w*h}, min={minA}, max={maxA}");
        }
    }

    /// <summary>Confirms the full-chain alpha assertion detects the authored extreme-height saturation case.</summary>
    [Fact]
    public void FullChain_ExtremeHeightStrength_WillSaturateAlpha_ProvingTestDetectsIt()
    {
        EnsureContextValid();
        fixture.MakeCurrent();

        // Arrange: retain the authored extreme-height bake parameters and checkerboard input.
        PbrHeightBakeTestPrograms.EnsureAssetsAvailable();
        using var programs = new PbrHeightBakeTestPrograms();
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

            var progL = programs.Create("pbr_heightbake_luminance");
            var progNorm = programs.Create("pbr_heightbake_normalize");
            var progPack = programs.Create("pbr_heightbake_pack_to_atlas");

            const int w = 32;
            const int h = 32;

            using var atlas = CreateCheckerboardAtlas(w, h, cell: 2);
            using var texL = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var texHn = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var outAtlas = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();

            // Act: exercise the production luminance, normalize, and pack passes.
            RenderTo(programs, texL, progL, (parameters) => {
                progL.atlas = atlas;


                    parameters.LuminanceAtlasRect = (x: 0, y: 0, w: w, h: h);
                    parameters.LuminanceDstSize = (width: w, height: h);
            });

            // Treat luminance as the solved height field, just to stress normalize+pack.
            float mean = MeanR32f(texL);
            (float invNeg, float invPos) = ComputeAsymmetricInvScales(texL, mean);
            RenderTo(programs, texHn, progNorm, (parameters) => {
                progNorm.h = texL;


                    parameters.CommonSize = (width: w, height: h);
                    parameters.NormalizeParams = (mean: mean, invNeg: invNeg, invPos: invPos, heightStrength: cfg.HeightStrength);
                    parameters.NormalizeGamma = cfg.Gamma;
            });

            RenderTo(programs, outAtlas, progPack, (parameters) => {
                progPack.height = texHn;

                progPack.albedoAtlas = atlas;


                    parameters.SolverSize = (width: w, height: h);
                    parameters.TileSize = (width: w, height: h);
                    parameters.ViewportOrigin = (x: 0, y: 0);
                    parameters.PackParams = (normalStrength: cfg.NormalStrength, normalScale: 1f, depthScale: 1f, eps: 0f);
            });

            // Assert: verify this stress case still demonstrates the suite's saturation detection.
            (_, float maxA, _, int ones) = AlphaStats(outAtlas);

            // This test is expected to show heavy saturation.
            Assert.True(maxA >= 0.99f);
            Assert.True(ones > (w * h * 0.25f), "Expected some alpha saturation at extreme height strength.");
        }
    }

    /// <summary>Verifies the production scale uniforms change the final packed output.</summary>
    [Fact]
    public void FullChain_ScaleUniforms_ChangeFinalPackedOutput()
    {
        EnsureContextValid();
        fixture.MakeCurrent();

        // Arrange: preserve the original shared input chain and both final output textures.
        PbrHeightBakeTestPrograms.EnsureAssetsAvailable();
        using var programs = new PbrHeightBakeTestPrograms();
        {
            var cfg = new VgeConfig.NormalDepthBakeConfig();

            var progL = programs.Create("pbr_heightbake_luminance");
            var progG = programs.Create("pbr_heightbake_gauss1d");
            var progSub = programs.Create("pbr_heightbake_sub");
            var progGrad = programs.Create("pbr_heightbake_gradient");
            var progDiv = programs.Create("pbr_heightbake_divergence");
            var progJacobi = programs.Create("pbr_heightbake_jacobi");
            var progNorm = programs.Create("pbr_heightbake_normalize");
            var progPack = programs.Create("pbr_heightbake_pack_to_atlas");

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

            // Act: run the shared production chain and pack each authored scale configuration.
            // 1) Luminance.
            RenderTo(programs, texL, progL, (parameters) => {
                progL.atlas = atlas;


                    parameters.LuminanceAtlasRect = (x: 0, y: 0, w: w, h: h);
                    parameters.LuminanceDstSize = (width: w, height: h);
            });

            // 2) base = Gauss(L, sigmaBig)
            RunGaussian1D(programs, texL, texTmp, progG, sigma: cfg.SigmaBig, dirX: true);
            RunGaussian1D(programs, texTmp, texBase, progG, sigma: cfg.SigmaBig, dirX: false);

            // 3) D0 = L - base
            RenderTo(programs, texD0, progSub, (parameters) => {
                progSub.a = texL;
                progSub.b = texBase;


                    parameters.CommonSize = (width: w, height: h);
                    parameters.GaussianParams = (radius: 0, relContrast: 0);
                    parameters.SubParams = (eps: 1e-6f, vMax: 1e6f);
            });

            // 4) Gradient
            RenderTo(programs, texGxy, progGrad, (parameters) => {
                progGrad.d = texD0;


                    parameters.CommonSize = (width: w, height: h);
                    parameters.GradientParams = (gain: cfg.Gain, maxSlope: cfg.MaxSlope, edgeT0: cfg.EdgeT0, edgeT1: cfg.EdgeT1);
            });

            // 5) Divergence
            RenderTo(programs, texDiv, progDiv, (parameters) => {
                progDiv.g = texGxy;


                    parameters.CommonSize = (width: w, height: h);
            });

            // 6) Jacobi iterations
            ClearR32f(texH, 0f);
            for (int i = 0; i < 150; i++)
            {
                DynamicTexture2D src = (i % 2 == 0) ? texH : texH2;
                DynamicTexture2D dst = (i % 2 == 0) ? texH2 : texH;

                RenderTo(programs, dst, progJacobi, (parameters) => {
                    progJacobi.h = src;
                    progJacobi.b = texDiv;


                        parameters.CommonSize = (width: w, height: h);
                });
            }

            // 7) Normalize
            float mean = MeanR32f(texH);
            (float invNeg, float invPos) = ComputeAsymmetricInvScales(texH, mean);
            RenderTo(programs, texHn, progNorm, (parameters) => {
                progNorm.h = texH;


                    parameters.CommonSize = (width: w, height: h);
                    parameters.NormalizeParams = (mean: mean, invNeg: invNeg, invPos: invPos, heightStrength: cfg.HeightStrength);
                    parameters.NormalizeGamma = cfg.Gamma;
            });

            // 8) Pack twice with different scales.
            RenderTo(programs, outAtlas1, progPack, (parameters) => {
                progPack.height = texHn;

                progPack.albedoAtlas = atlas;


                    parameters.SolverSize = (width: w, height: h);
                    parameters.TileSize = (width: w, height: h);
                    parameters.ViewportOrigin = (x: 0, y: 0);
                    parameters.PackParams = (normalStrength: cfg.NormalStrength, normalScale: 1f, depthScale: 1f, eps: 0f);
            });

            RenderTo(programs, outAtlas2, progPack, (parameters) => {
                progPack.height = texHn;

                progPack.albedoAtlas = atlas;


                    parameters.SolverSize = (width: w, height: h);
                    parameters.TileSize = (width: w, height: h);
                    parameters.ViewportOrigin = (x: 0, y: 0);
                    parameters.PackParams = (normalStrength: cfg.NormalStrength, normalScale: 0.5f, depthScale: 0.5f, eps: 0f);
            });

            // The original maxima exceed their thresholds exactly when at least one corresponding component does.
            // Assert: retain both independent output predicates and their original pixel checks.
            Assert.True(ImageAssertions.Predicates.Predicate(outAtlas1, MaterialPredicates.PbrHeightBakeFullChainTests_FullChain_ScaleUniforms_ChangeFinalPackedOutput,
                outAtlas2).MatchingTexels>0,"Expected alpha to change with depthScale.");
            Assert.True(ImageAssertions.Predicates.Predicate(outAtlas1, MaterialPredicates.PbrHeightBakeFullChainTests_FullChain_ScaleUniforms_ChangeFinalPackedOutput_2,
                outAtlas2).MatchingTexels>0,"Expected RGB to change with normalScale.");
        }
    }

    /// <summary>Checks the normal-scale-zero behavior of the production normal/depth packing stages.</summary>
    [Fact]
    public void FullChain_NormalScaleZero_FlattensPackedNormalsRgb()
    {
        EnsureContextValid();
        fixture.MakeCurrent();

        // Arrange: retain the common bake inputs and the zero/non-zero normal scale outputs.
        PbrHeightBakeTestPrograms.EnsureAssetsAvailable();
        using var programs = new PbrHeightBakeTestPrograms();
        {
            var cfg = new VgeConfig.NormalDepthBakeConfig
            {
                // Ensure normals are actually generated (otherwise normalScale doesn't matter).
                NormalStrength = 4f
            };

            var progL = programs.Create("pbr_heightbake_luminance");
            var progG = programs.Create("pbr_heightbake_gauss1d");
            var progSub = programs.Create("pbr_heightbake_sub");
            var progGrad = programs.Create("pbr_heightbake_gradient");
            var progDiv = programs.Create("pbr_heightbake_divergence");
            var progJacobi = programs.Create("pbr_heightbake_jacobi");
            var progNorm = programs.Create("pbr_heightbake_normalize");
            var progPack = programs.Create("pbr_heightbake_pack_to_atlas");

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

            // Act: execute the production chain and pack flat and scaled normal results.
            // 1) Luminance.
            RenderTo(programs, texL, progL, (parameters) => {
                progL.atlas = atlas;


                    parameters.LuminanceAtlasRect = (x: 0, y: 0, w: w, h: h);
                    parameters.LuminanceDstSize = (width: w, height: h);
            });

            // 2) base = Gauss(L, sigmaBig)
            RunGaussian1D(programs, texL, texTmp, progG, sigma: cfg.SigmaBig, dirX: true);
            RunGaussian1D(programs, texTmp, texBase, progG, sigma: cfg.SigmaBig, dirX: false);

            // 3) D0 = L - base
            RenderTo(programs, texD0, progSub, (parameters) => {
                progSub.a = texL;
                progSub.b = texBase;


                    parameters.CommonSize = (width: w, height: h);
                    parameters.GaussianParams = (radius: 0, relContrast: 0);
                    parameters.SubParams = (eps: 1e-6f, vMax: 1e6f);
            });

            // 4) Gradient
            RenderTo(programs, texGxy, progGrad, (parameters) => {
                progGrad.d = texD0;


                    parameters.CommonSize = (width: w, height: h);
                    parameters.GradientParams = (gain: cfg.Gain, maxSlope: cfg.MaxSlope, edgeT0: cfg.EdgeT0, edgeT1: cfg.EdgeT1);
            });

            // 5) Divergence
            RenderTo(programs, texDiv, progDiv, (parameters) => {
                progDiv.g = texGxy;


                    parameters.CommonSize = (width: w, height: h);
            });

            // 6) Jacobi iterations
            ClearR32f(texH, 0f);
            for (int i = 0; i < 150; i++)
            {
                DynamicTexture2D src = (i % 2 == 0) ? texH : texH2;
                DynamicTexture2D dst = (i % 2 == 0) ? texH2 : texH;

                RenderTo(programs, dst, progJacobi, (parameters) => {
                    progJacobi.h = src;
                    progJacobi.b = texDiv;


                        parameters.CommonSize = (width: w, height: h);
                });
            }

            // 7) Normalize
            float mean = MeanR32f(texH);
            (float invNeg, float invPos) = ComputeAsymmetricInvScales(texH, mean);
            RenderTo(programs, texHn, progNorm, (parameters) => {
                progNorm.h = texH;


                    parameters.CommonSize = (width: w, height: h);
                    parameters.NormalizeParams = (mean: mean, invNeg: invNeg, invPos: invPos, heightStrength: cfg.HeightStrength);
                    parameters.NormalizeGamma = cfg.Gamma;
            });

            // 8) Pack with normalScale=0 (flat) and normalScale=1 (non-flat).
            RenderTo(programs, outAtlasFlat, progPack, (parameters) => {
                progPack.height = texHn;

                progPack.albedoAtlas = atlas;


                    parameters.SolverSize = (width: w, height: h);
                    parameters.TileSize = (width: w, height: h);
                    parameters.ViewportOrigin = (x: 0, y: 0);
                    parameters.PackParams = (normalStrength: cfg.NormalStrength, normalScale: 0f, depthScale: 1f, eps: 0f);
            });

            RenderTo(programs, outAtlasScaled, progPack, (parameters) => {
                progPack.height = texHn;

                progPack.albedoAtlas = atlas;


                    parameters.SolverSize = (width: w, height: h);
                    parameters.TileSize = (width: w, height: h);
                    parameters.ViewportOrigin = (x: 0, y: 0);
                    parameters.PackParams = (normalStrength: cfg.NormalStrength, normalScale: 1f, depthScale: 1f, eps: 0f);
            });

            // Assert: retain the flat RGB predicate and the original scaled-output witness.
            ImageAssertions.Predicates.Predicate(outAtlasFlat, MaterialPredicates.PbrHeightBakeFullChainTests_FullChain_NormalScaleZero_FlattensPackedNormalsRgb).AssertEqual();
            Assert.True(ImageAssertions.Predicates.Predicate(outAtlasFlat, MaterialPredicates.PbrHeightBakeFullChainTests_FullChain_ScaleUniforms_ChangeFinalPackedOutput_2,
                outAtlasScaled).MatchingTexels>0,"Expected normalScale=0 vs 1 to change RGB.");
        }
    }

    #endregion

    #region Private
    #region Pass submission
    /// <summary>Assigns typed production parameters and executes one cleared bake pass.</summary>
    private void RenderTo(PbrHeightBakeTestPrograms programs, DynamicTexture2D dst, PbrHeightBakeShaderProgram program, Action<PbrHeightBakeParamsUbo> setup)
    {
        setup(programs.Parameters);
        programs.Draw(dst, program, clear: true);
    }

    /// <summary>Executes one Gaussian axis using the production block's 64-weight capacity.</summary>
    private void RunGaussian1D(PbrHeightBakeTestPrograms programs, DynamicTexture2D src, DynamicTexture2D dst, PbrHeightBakeShaderProgram program, float sigma, bool dirX)
    {
        int radius = Math.Clamp((int)Math.Ceiling(3.0 * sigma), 0, 63);
        float[] weights = BuildGaussianWeights(sigma);

        RenderTo(programs, dst, program, parameters =>
        {
            program.src = src;
            parameters.CommonSize = (dst.Width, dst.Height);
            parameters.GaussianDirection = (dirX ? 1 : 0, dirX ? 0 : 1);
            parameters.GaussianParams = (radius: radius, relContrast: 0);
            parameters.SetKernelWeights(weights, count: 64);
        });
    }
    #endregion

    #region Input and normalization helpers
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

    /// <summary>Initializes a scalar height field through the RHI texture-clear command.</summary>
    private void ClearR32f(DynamicTexture2D tex, float value)
    {
        Span<float> clear = stackalloc float[1] { value };
        RHICommandContext.Current.ClearTexture(tex, new(new(), 0, 0, 0, tex.Width, tex.Height),
            new(PixelChannelLayout.Red, PixelComponentType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes(clear));
    }

    /// <summary>Returns the mean of every original scalar through a bounded double GPU sum.</summary>
    private float MeanR32f(DynamicTexture2D tex)
    {
        return (float)(ImageAssertions.Statistics.Statistics(tex,channelMask:1).Sum(0)/(tex.Width*tex.Height));
    }

    /// <summary>Derives authored normalization inputs from complete-domain extrema without downloading the height field.</summary>
    private (float invNeg, float invPos) ComputeAsymmetricInvScales(DynamicTexture2D tex, float center)
    {
        var extrema=ImageAssertions.Statistics.Inspect(tex,channelMask:1);
        float min=(float)extrema.Minimum,max=(float)extrema.Maximum;

        const float eps = 1e-6f;
        const float maxInv = 64f;
        float negSpan = center - min;
        float posSpan = max - center;
        float invNeg = negSpan > eps ? Math.Min(1f / negSpan, maxInv) : 0f;
        float invPos = posSpan > eps ? Math.Min(1f / posSpan, maxInv) : 0f;
        return (invNeg, invPos);
    }

    /// <summary>Builds the authored RGBA checkerboard atlas used by the bake scenarios.</summary>
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
    #endregion

    #region Image assertions and observations
    /// <summary>Checks the original first-channel finite values and variance, retaining float products with double sums.</summary>
    private void AssertFiniteAndHasVariance(DynamicTexture2D tex, int channels, string name)
    {
        var moments=ImageAssertions.Statistics.Statistics(tex,channelMask:1,squareInFloat:true);
        Assert.True(moments.NonFinite==0,name+": "+moments);
        int count=tex.Width*tex.Height;
        double mean=moments.Sum(0)/count;
        double variance=Math.Max(0,moments.SumSquares(0)/count-mean*mean);
        Assert.True(variance>1e-8,$"{name} variance too low: {variance}");
    }

    /// <summary>Checks the original first-channel finite contract over the complete image.</summary>
    private void AssertFinite(DynamicTexture2D tex, int channels, string name)
    {
        var observation=ImageAssertions.Statistics.Inspect(tex,channelMask:1);
        Assert.True(observation.NonFinite==0,name+": "+observation);
    }

    /// <summary>Retains every alpha value's finite classification, extrema and strict white-saturation threshold.</summary>
    private (float minA, float maxA, int nanA, int ones) AlphaStats(GpuTexture image)
    {
        var observation=ImageAssertions.Statistics.Inspect(image,channelMask:8);
        uint ones=ImageAssertions.Predicates.Predicate(image, MaterialPredicates.PbrHeightBakeFullChainTests_AlphaStats).MatchingTexels;
        return ((float)observation.Minimum,(float)observation.Maximum,(int)observation.NonFinite,(int)ones);
    }
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.GPU.Assertions.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("inspection", 0, 0);
        programs.Prepare("predicate-material", 0, 0);
        programs.Prepare("projection-observation", 0, 0);
        programs.Prepare("statistics", 0, 0);
    }
    #endregion
    #endregion
}
