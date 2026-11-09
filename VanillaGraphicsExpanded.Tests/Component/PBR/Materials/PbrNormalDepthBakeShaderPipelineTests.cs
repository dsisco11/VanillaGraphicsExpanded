using VanillaGraphicsExpanded.Tests.Component.PBR.Materials.Fixtures;
using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

using Xunit;



namespace VanillaGraphicsExpanded.Tests.Component.PBR.Materials;
/// <summary>Qualifies the production height-bake normalization and atlas-packing outputs through the RHI.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class PbrNormalDepthBakeShaderPipelineTests : RenderTestBase
{
    #region Public API

    /// <summary>Uses the shared headless fixture for production bake shader execution.</summary>
    public PbrNormalDepthBakeShaderPipelineTests(HeadlessGLFixture fixture) : base(fixture) { }

    /// <summary>Height packing clamps signed input into alpha while preserving the flat normal encoding.</summary>
    [Fact]
    public void PackToAtlas_EncodesHeightIntoAlpha01_NotAllWhite()
    {
        // Arrange
        EnsureContextValid();
        PbrHeightBakeTestPrograms.EnsureAssetsAvailable();
        using var programs = new PbrHeightBakeTestPrograms();
        var program = programs.Create("pbr_heightbake_pack_to_atlas");

        const int w = 16;
        const int h = 8;

        // Signed height ramp across X in [-2..2]. The packing shader clamps to [-1..1] and encodes to [0..1].
        float[] heightData = new float[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float t = w == 1 ? 0f : (float)x / (w - 1);
                heightData[y * w + x] = t * 4f - 2f;
            }
        }

        using var heightTex = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = w, Height = h, Format = TextureFormat.R32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, w, h), new RHIPixelEncoding(TextureFormatHelper.GetPixelFormat(TextureFormat.R32f), PixelComponentType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((heightData).AsSpan()).ToArray())]).RequireResource();
        using var target = CreateRenderTarget(w, h, TextureFormat.Rgba16f);

        // Ensure the pack shader does not take the alpha-cutoff early-out path.
        using var albedoAtlas = CreateSolidAlbedoAtlas(w, h, alpha: 1f);
        ConfigurePack(programs, program, heightTex, albedoAtlas, w, h, normalStrength: 0f, normalScale: 1f, depthScale: 1f);

        // Act
        programs.Draw(target[0], program, clear: true);

        // Assert
        var alpha=ImageAssertions.Statistics.Inspect(target[0],channelMask:8);
        float minA=(float)alpha.Minimum,maxA=(float)alpha.Maximum;

        // Sanity: should vary and stay in a renderdoc-friendly range.
        Assert.True(maxA - minA > 0.25f, $"Alpha appears constant; min={minA}, max={maxA}");
        Assert.InRange(minA, -0.01f, 0.10f);
        Assert.InRange(maxA, 0.90f, 1.01f);

        // Spot-check RGB normal output when normalStrength=0: should be approximately (0.5, 0.5, 1.0).
        Assert.InRange(GpuLocalRead.Scalar(target[0],0), 0.49f, 0.51f);
        Assert.InRange(GpuLocalRead.Scalar(target[0],1), 0.49f, 0.51f);
        Assert.InRange(GpuLocalRead.Scalar(target[0],2), 0.99f, 1.01f);
    }

    /// <summary>Depth scale reduces the packed alpha range for the same centered source ramp.</summary>
    [Fact]
    public void PackToAtlas_DepthScale_AffectsAlphaRange_ForCenteredInput()
    {
        // Arrange
        EnsureContextValid();
        PbrHeightBakeTestPrograms.EnsureAssetsAvailable();
        using var programs = new PbrHeightBakeTestPrograms();
        var program = programs.Create("pbr_heightbake_pack_to_atlas");

        const int w = 16;
        const int h = 8;

        // Centered ramp in [-1..1] so depthScale directly reduces alpha range.
        float[] heightData = new float[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float t = w == 1 ? 0f : (float)x / (w - 1);
                heightData[y * w + x] = t * 2f - 1f;
            }
        }
        using var heightTex = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = w, Height = h, Format = TextureFormat.R32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, w, h), new RHIPixelEncoding(TextureFormatHelper.GetPixelFormat(TextureFormat.R32f), PixelComponentType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((heightData).AsSpan()).ToArray())]).RequireResource();
        using var albedoAtlas = CreateSolidAlbedoAtlas(w, h, alpha: 1f);


        // Act
        (float minA1, float maxA1) = RenderPackAndGetAlphaMinMax(programs, program, heightTex, albedoAtlas, w, h, normalStrength: 0f, normalScale: 1f, depthScale: 1f);
        (float minA2, float maxA2) = RenderPackAndGetAlphaMinMax(programs, program, heightTex, albedoAtlas, w, h, normalStrength: 0f, normalScale: 1f, depthScale: 0.5f);

        // Assert
        float range1 = maxA1 - minA1;
        float range2 = maxA2 - minA2;

        Assert.True(range1 > 0.80f, $"Expected wide alpha range at depthScale=1; min={minA1}, max={maxA1}");
        Assert.True(range2 < range1 * 0.75f, $"Expected narrower alpha range at depthScale=0.5; range1={range1}, range2={range2}");
    }

    /// <summary>Normal scale changes encoded RGB while zero normal strength remains flat.</summary>
    [Fact]
    public void PackToAtlas_NormalScale_AffectsRgb_WhenNormalStrengthNonZero()
    {
        // Arrange
        EnsureContextValid();
        PbrHeightBakeTestPrograms.EnsureAssetsAvailable();
        using var programs = new PbrHeightBakeTestPrograms();
        var program = programs.Create("pbr_heightbake_pack_to_atlas");

        const int w = 16;
        const int h = 8;

        // Linear ramp gives a constant slope -> stable normal.
        float[] heightData = new float[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float t = w == 1 ? 0f : (float)x / (w - 1);
                heightData[y * w + x] = t * 2f - 1f;
            }
        }
        using var heightTex = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = w, Height = h, Format = TextureFormat.R32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, w, h), new RHIPixelEncoding(TextureFormatHelper.GetPixelFormat(TextureFormat.R32f), PixelComponentType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((heightData).AsSpan()).ToArray())]).RequireResource();
        using var albedoAtlas = CreateSolidAlbedoAtlas(w, h, alpha: 1f);



        // Act
        float[] rgbFlat = RenderPackAndReadFirstPixel(programs, program, heightTex, albedoAtlas, w, h, normalStrength: 1f, normalScale: 0f, depthScale: 1f);
        float[] rgbTilt = RenderPackAndReadFirstPixel(programs, program, heightTex, albedoAtlas, w, h, normalStrength: 1f, normalScale: 2f, depthScale: 1f);

        // Assert
        // normalScale=0 should be flat (0.5,0.5,1)
        Assert.InRange(rgbFlat[0], 0.49f, 0.51f);
        Assert.InRange(rgbFlat[1], 0.49f, 0.51f);
        Assert.InRange(rgbFlat[2], 0.99f, 1.01f);

        // With a non-zero scale, the normal should tilt away from (0.5,0.5,1).
        Assert.True(Math.Abs(rgbTilt[0] - 0.5f) > 0.01f || Math.Abs(rgbTilt[2] - 1.0f) > 0.01f,
            $"Expected tilted normal; got ({rgbTilt[0]}, {rgbTilt[1]}, {rgbTilt[2]})");
    }

    /// <summary>Production normalization followed by packing preserves a nonconstant bipolar alpha field.</summary>
    [Fact]
    public void NormalizeThenPack_ProducesNonFlatAlpha_ForBipolarInput()
    {
        // Arrange
        EnsureContextValid();
        PbrHeightBakeTestPrograms.EnsureAssetsAvailable();
        using var programs = new PbrHeightBakeTestPrograms();
        var normalizeProgram = programs.Create("pbr_heightbake_normalize");
        var packProgram = programs.Create("pbr_heightbake_pack_to_atlas");

        const int w = 32;
        const int h = 16;

        // Bipolar input around zero so a correct mean subtraction should produce both signs.
        float[] hData = new float[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float fx = (float)x / (w - 1);
                float fy = (float)y / (h - 1);
                // Centered-ish pattern in [-1..1]
                hData[y * w + x] = (fx - 0.5f) * 2f + (fy - 0.5f) * 0.5f;
            }
        }

        using var hTex = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = w, Height = h, Format = TextureFormat.R32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, w, h), new RHIPixelEncoding(TextureFormatHelper.GetPixelFormat(TextureFormat.R32f), PixelComponentType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((hData).AsSpan()).ToArray())]).RequireResource();
        using var hnTex = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, w), Height = System.Math.Max(1, h), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();

        // Act
        // Pass 1: normalize into hnTex
        normalizeProgram.h = hTex;
        programs.Parameters.CommonSize = (w, h);
        programs.Parameters.NormalizeParams = (0f, 1f, 1f, 1f);
        programs.Parameters.NormalizeGamma = 1f;
        programs.Draw(hnTex, normalizeProgram, clear: true);

        // Pass 2: pack into RGBA16F output
        using var outFbo = CreateRenderTarget(w, h, TextureFormat.Rgba16f);
        using var albedoAtlas2 = CreateSolidAlbedoAtlas(w, h, alpha: 1f);
        ConfigurePack(programs, packProgram, hnTex, albedoAtlas2, w, h, normalStrength: 0f, normalScale: 1f, depthScale: 1f);
        programs.Draw(outFbo[0], packProgram, clear: true);

        // Assert
        var alpha=ImageAssertions.Statistics.Inspect(outFbo[0],channelMask:8);
        float minA=(float)alpha.Minimum,maxA=(float)alpha.Maximum;
        uint ones=ImageAssertions.Predicates.Predicate(outFbo[0], MaterialPredicates.PbrNormalDepthBakeShaderPipelineTests_NormalizeThenPack_ProducesNonFlatAlpha_ForBipolarInput).MatchingTexels;
        uint zeros=ImageAssertions.Predicates.Predicate(outFbo[0], MaterialPredicates.PbrNormalDepthBakeShaderPipelineTests_NormalizeThenPack_ProducesNonFlatAlpha_ForBipolarInput_2).MatchingTexels;

        Assert.True(maxA - minA > 0.10f, $"Alpha too flat; min={minA}, max={maxA}");
        Assert.True(ones < (w * h * 0.95), $"Alpha appears saturated to 1.0; ones={ones}/{w*h}");
        Assert.True(zeros < (w * h * 0.95), $"Alpha appears saturated to 0.0; zeros={zeros}/{w*h}");
    }

    /// <summary>The production pack pass retains nonwhite alpha under explicit additive blending.</summary>
    [Fact]
    public void PackToAtlas_WithBlendEnabled_DoesNotTurnAlphaSolidWhite()
    {
        // Arrange
        EnsureContextValid();
        PbrHeightBakeTestPrograms.EnsureAssetsAvailable();
        using var programs = new PbrHeightBakeTestPrograms();
        var program = programs.Create("pbr_heightbake_pack_to_atlas");

        const int w = 32;
        const int h = 16;

        float[] heightData = new float[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float t = (float)x / (w - 1);
                heightData[y * w + x] = t * 2f - 1f;
            }
        }


        using var heightTex = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = w, Height = h, Format = TextureFormat.R32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, w, h), new RHIPixelEncoding(TextureFormatHelper.GetPixelFormat(TextureFormat.R32f), PixelComponentType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((heightData).AsSpan()).ToArray())]).RequireResource();
        using var target = CreateRenderTarget(w, h, TextureFormat.Rgba16f);
        using var albedoAtlas = CreateSolidAlbedoAtlas(w, h, alpha: 1f);
        ConfigurePack(programs, program, heightTex, albedoAtlas, w, h, normalStrength: 0f, normalScale: 1f, depthScale: 1f);
        var additive = new ColorBlendDesc { Enabled = true, SourceRgb = BlendFactor.One,
            DestinationRgb = BlendFactor.One, SourceAlpha = BlendFactor.One, DestinationAlpha = BlendFactor.One };

        // Act
        programs.Draw(target[0], program, clear: true, blend: additive);

        // Assert
        uint ones=ImageAssertions.Predicates.Predicate(target[0], MaterialPredicates.PbrNormalDepthBakeShaderPipelineTests_NormalizeThenPack_ProducesNonFlatAlpha_ForBipolarInput).MatchingTexels;

        Assert.True(ones < (w * h * 0.98), $"Alpha appears solid white under blending; ones={ones}/{w*h}");
    }

    #endregion

    #region Protected

    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("inspection", 0, 0);
        programs.Prepare("predicate-material", 0, 0);
    }

    #endregion

    #region Private

    /// <summary>Renders one production pack pass and returns the complete output alpha range.</summary>
    private (float minA, float maxA) RenderPackAndGetAlphaMinMax(PbrHeightBakeTestPrograms programs,
        PbrHeightBakeShaderProgram program, DynamicTexture2D heightTex, DynamicTexture2D albedoAtlas,
        int w, int h, float normalStrength, float normalScale, float depthScale)
    {
        using var target = CreateRenderTarget(w, h, TextureFormat.Rgba16f);
        ConfigurePack(programs, program, heightTex, albedoAtlas, w, h, normalStrength, normalScale, depthScale);
        programs.Draw(target[0], program, clear: true);

        var alpha = ImageAssertions.Statistics.Inspect(target[0], channelMask: 8);
        float minA = (float)alpha.Minimum, maxA = (float)alpha.Maximum;

        return (minA, maxA);
    }

    /// <summary>Renders one production pack pass and observes the authored first output pixel.</summary>
    private float[] RenderPackAndReadFirstPixel(PbrHeightBakeTestPrograms programs, PbrHeightBakeShaderProgram program,
        DynamicTexture2D heightTex, DynamicTexture2D albedoAtlas, int w, int h,
        float normalStrength, float normalScale, float depthScale)
    {
        using var target = CreateRenderTarget(w, h, TextureFormat.Rgba16f);
        ConfigurePack(programs, program, heightTex, albedoAtlas, w, h, normalStrength, normalScale, depthScale);
        programs.Draw(target[0], program, clear: true);

        float[] pixels = target[0].ReadPixelsRegion(0, 0, 1, 1);
        return new[] { pixels[0], pixels[1], pixels[2] };
    }

    /// <summary>Captures the production pack inputs and full-resolution parameter block for one draw.</summary>
    private static void ConfigurePack(PbrHeightBakeTestPrograms programs, PbrHeightBakeShaderProgram program,
        GpuTextureRef height, GpuTextureRef albedoAtlas, int width, int heightPixels,
        float normalStrength, float normalScale, float depthScale)
    {
        program.height = height;
        program.albedoAtlas = albedoAtlas;
        programs.Parameters.SolverSize = (width, heightPixels);
        programs.Parameters.TileSize = (width, heightPixels);
        programs.Parameters.ViewportOrigin = (0, 0);
        programs.Parameters.PackParams = (normalStrength, normalScale, depthScale, 0f);
    }

    /// <summary>Creates the constant opaque albedo page required to keep the production pack path active.</summary>
    private static DynamicTexture2D CreateSolidAlbedoAtlas(int w, int h, float alpha)
    {
        float[] rgba = new float[w * h * 4];
        for (int i = 0; i < rgba.Length; i += 4)
        {
            rgba[i + 0] = 1f;
            rgba[i + 1] = 1f;
            rgba[i + 2] = 1f;
            rgba[i + 3] = alpha;
        }

        return RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = w, Height = h, Format = TextureFormat.Rgba8, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = "Tests.Pbr.NormalDepthBake.AlbedoAtlas" }, [new RHITextureInitialData(new(new(), 0, 0, 0, w, h), new RHIPixelEncoding(TextureFormatHelper.GetPixelFormat(TextureFormat.Rgba8), PixelComponentType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((rgba).AsSpan()).ToArray())]).RequireResource();
    }

    #endregion
}
