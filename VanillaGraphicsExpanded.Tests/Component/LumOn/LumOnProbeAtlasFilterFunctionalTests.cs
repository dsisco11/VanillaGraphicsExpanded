using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.LumOn;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Rendering;
using Xunit;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>
/// Functional tests for the LumOn probe-atlas filter shader pass.
///
/// Verifies that the filter:
/// - smooths within-tile noise (when hit distances match)
/// - preserves edges via hit-distance-based edge stopping
/// - respects probe validity (invalid probes output zero)
/// - respects per-texel confidence (low-confidence samples do not bleed)
/// </summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public class LumOnProbeAtlasFilterFunctionalTests : LumOnShaderFunctionalTestBase
{


    #region Public API
    /// <summary>Creates the fixture-backed probe-atlas filter suite.</summary>
    public LumOnProbeAtlasFilterFunctionalTests(HeadlessGLFixture fixture) : base(fixture) { }
    /// <summary>Invalid probe tiles filter to zero while adjacent valid tiles retain their radiance.</summary>
    [Fact]
    public void Filter_RespectsInvalidProbes()
    {
        // Arrange
        EnsureShaderTestAvailable();

        // Hit flag (matches shader include: LUMON_META_HIT = 1u<<0)
        const uint HitFlag = 1u;

        var anchorPos = CreateValidProbeAnchors(1.0f);
        SetAnchorValidity(anchorPos, probeX: 0, probeY: 0, valid: 0.0f);

        var atlas = CreateUniformAtlas(1f, 0f, 0f, hitDistance: 10f);
        var meta = CreateUniformMetaAtlas(confidence: 1.0f, flags: HitFlag);

        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorPos);
        using var atlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rgba16f, atlas);
        using var metaTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rg32f, meta);

        using var outputAtlas = TestFramework.CreateTestGBuffer(
            AtlasWidth, AtlasHeight,
            TextureFormat.Rgba16f,
            TextureFormat.Rg32f);

        var programId = CompileProbeAtlasFilterShader();

        SetupUniforms(programId, filterRadius: 1, hitDistanceSigma: 1.0f);

        programId.ScreenProbeAtlas = atlasTex;
        programId.ScreenProbeAtlasMeta = metaTex;
        using var anchorInputs101 = LayeredTestTexture.Create(ImageAssertions.Programs, anchorPosTex, null);
        programId.ProbeAnchors = anchorInputs101;

        // Act
        TestFramework.RenderQuadTo(programId, outputAtlas);
        var outRgba = outputAtlas[0];

        // Invalid probe (0,0) tile covers [0..7]x[0..7]
        var (r0, g0, b0, _) = ReadAtlasTexel(outRgba, 4, 4);
        // Assert
        Assert.True(r0 < 0.01f && g0 < 0.01f && b0 < 0.01f, "Invalid probe tile should output zero");

        // Valid probe (1,0) tile covers [8..15]x[0..7]
        var (r1, g1, b1, _) = ReadAtlasTexel(outRgba, 12, 4);
        Assert.True(r1 > 0.9f && g1 < 0.1f && b1 < 0.1f, "Valid probe tile should preserve non-zero radiance");
    }

    /// <summary>A zero-confidence neighbor contributes no color to the filtered probe output.</summary>
    [Fact]
    public void Filter_RespectsConfidence()
    {
        // Arrange
        EnsureShaderTestAvailable();

        const uint HitFlag = 1u;

        var anchorPos = CreateValidProbeAnchors(1.0f);

        // Start fully red
        var atlas = CreateUniformAtlas(1f, 0f, 0f, hitDistance: 10f);
        var meta = CreateUniformMetaAtlas(confidence: 1.0f, flags: HitFlag);

        // Add a blue neighbor with ZERO confidence (should not bleed)
        // Center at (4,4) in probe (0,0) tile
        SetAtlasTexel(atlas, x: 5, y: 4, r: 0f, g: 0f, b: 1f, hitDistance: 10f);
        SetMetaTexel(meta, x: 5, y: 4, confidence: 0.0f, flags: HitFlag);


        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorPos);
        using var atlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rgba16f, atlas);
        using var metaTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rg32f, meta);
        using var outputAtlas = TestFramework.CreateTestGBuffer(
            AtlasWidth, AtlasHeight,
            TextureFormat.Rgba16f,
            TextureFormat.Rg32f);

        var programId = CompileProbeAtlasFilterShader();

        // Large sigma so hit distance does not reject; this isolates confidence weighting.
        SetupUniforms(programId, filterRadius: 1, hitDistanceSigma: 1000.0f);

        programId.ScreenProbeAtlas = atlasTex;
        programId.ScreenProbeAtlasMeta = metaTex;
        using var anchorInputs102 = LayeredTestTexture.Create(ImageAssertions.Programs, anchorPosTex, null);
        programId.ProbeAnchors = anchorInputs102;

        // Act
        TestFramework.RenderQuadTo(programId, outputAtlas);
        var outRgba = outputAtlas[0];

        var (r, g, b, _) = ReadAtlasTexel(outRgba, 4, 4);
        // Assert
        Assert.True(r > 0.9f && g < 0.1f && b < 0.1f,
            $"Low-confidence neighbor should not bleed; got ({r:F3},{g:F3},{b:F3})");
    }

    /// <summary>Filtering reduces same-surface color noise while preserving a hit-distance edge.</summary>
    [Fact]
    public void Filter_ReducesNoise_PreservesEdges()
    {
        // Arrange
        EnsureShaderTestAvailable();

        const uint HitFlag = 1u;

        var anchorPos = CreateValidProbeAnchors(1.0f);

        // Case A: Noise reduction when hit distances match
        {
            var atlas = CreateUniformAtlas(1f, 0f, 0f, hitDistance: 10f);
            var meta = CreateUniformMetaAtlas(confidence: 1.0f, flags: HitFlag);

            // Make a local 3x3 checker around (4,4) in probe (0,0) tile
            // Alternate red/blue (same hit distance) so output should be mixed.
            int cx = 4, cy = 4;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    bool blue = ((dx + dy) & 1) == 0;
                    SetAtlasTexel(atlas, cx + dx, cy + dy, blue ? 0f : 1f, 0f, blue ? 1f : 0f, hitDistance: 10f);
                }
            }


            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorPos);
            using var atlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rgba16f, atlas);
            using var metaTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rg32f, meta);
            using var outputAtlas = TestFramework.CreateTestGBuffer(
                AtlasWidth, AtlasHeight,
                TextureFormat.Rgba16f,
                TextureFormat.Rg32f);

            var programId = CompileProbeAtlasFilterShader();

            SetupUniforms(programId, filterRadius: 1, hitDistanceSigma: 1000.0f);

            programId.ScreenProbeAtlas = atlasTex;
            programId.ScreenProbeAtlasMeta = metaTex;
            using var anchorInputs103 = LayeredTestTexture.Create(ImageAssertions.Programs, anchorPosTex, null);
            programId.ProbeAnchors = anchorInputs103;

            // Act
            TestFramework.RenderQuadTo(programId, outputAtlas);
            var outRgba = outputAtlas[0];

            var (r, _, b, _) = ReadAtlasTexel(outRgba, 4, 4);
            // Assert
            Assert.True(r > 0.3f && r < 0.7f && b > 0.3f && b < 0.7f,
                $"Expected smoothing toward a mix; got R={r:F3}, B={b:F3}");
        }

        // Case B: Edge preservation via hit-distance stopping
        {
            var atlas = CreateUniformAtlas(1f, 0f, 0f, hitDistance: 10f);
            var meta = CreateUniformMetaAtlas(confidence: 1.0f, flags: HitFlag);

            // Neighbor has a very different hit distance and different color.
            SetAtlasTexel(atlas, x: 5, y: 4, r: 0f, g: 1f, b: 0f, hitDistance: 200f);


            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorPos);
            using var atlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rgba16f, atlas);
            using var metaTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rg32f, meta);
            using var outputAtlas = TestFramework.CreateTestGBuffer(
                AtlasWidth, AtlasHeight,
                TextureFormat.Rgba16f,
                TextureFormat.Rg32f);

            var programId = CompileProbeAtlasFilterShader();

            // Small sigma => strong edge stop on large hit-distance delta.
            SetupUniforms(programId, filterRadius: 1, hitDistanceSigma: 0.05f);

            programId.ScreenProbeAtlas = atlasTex;
            programId.ScreenProbeAtlasMeta = metaTex;
            using var anchorInputs104 = LayeredTestTexture.Create(ImageAssertions.Programs, anchorPosTex, null);
            programId.ProbeAnchors = anchorInputs104;

            // Act
            TestFramework.RenderQuadTo(programId, outputAtlas);
            var outRgba = outputAtlas[0];

            var (r, g, b, _) = ReadAtlasTexel(outRgba, 4, 4);
            // Assert
            Assert.True(r > 0.9f && g < 0.1f && b < 0.1f,
                $"Expected edge-stopped result to remain near center; got ({r:F3},{g:F3},{b:F3})");
        }
    }
    #endregion
    #region Protected API
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("layer", 0, 0);
    }
    #endregion

    #region Private


    /// <summary>Creates the production probe-atlas filter shader program.</summary>
    private LumOnScreenProbeAtlasFilterShaderProgram CompileProbeAtlasFilterShader() => Programs.Create<LumOnScreenProbeAtlasFilterShaderProgram>();

    /// <summary>Encodes a positive hit distance using the atlas logarithmic distance representation.</summary>
    private static float EncodeHitDistance(float distance) => (float)Math.Log(distance + 1.0);

    /// <summary>Reinterprets packed metadata bits as a floating-point texture component.</summary>
    private static float FlagsToFloat(uint flags) => BitConverter.UInt32BitsToSingle(flags);

    /// <summary>Creates a probe-anchor image whose positions match their grid coordinates.</summary>
    private static float[] CreateValidProbeAnchors(float valid = 1.0f)
    {
        var data = new float[ProbeGridWidth * ProbeGridHeight * 4];
        for (int y = 0; y < ProbeGridHeight; y++)
        {
            for (int x = 0; x < ProbeGridWidth; x++)
            {
                int idx = (y * ProbeGridWidth + x) * 4;
                data[idx + 0] = x;
                data[idx + 1] = y;
                data[idx + 2] = 0f;
                data[idx + 3] = valid;
            }
        }
        return data;
    }

    /// <summary>Changes the validity channel for one probe in the authored anchor image.</summary>
    private static void SetAnchorValidity(float[] anchorData, int probeX, int probeY, float valid)
    {
        int idx = (probeY * ProbeGridWidth + probeX) * 4 + 3;
        anchorData[idx] = valid;
    }

    /// <summary>Creates an atlas initialized to one radiance color and encoded hit distance.</summary>
    private static float[] CreateUniformAtlas(float r, float g, float b, float hitDistance)
    {
        float a = EncodeHitDistance(hitDistance);
        var data = new float[AtlasWidth * AtlasHeight * 4];
        for (int i = 0; i < AtlasWidth * AtlasHeight; i++)
        {
            int idx = i * 4;
            data[idx + 0] = r;
            data[idx + 1] = g;
            data[idx + 2] = b;
            data[idx + 3] = a;
        }
        return data;
    }

    /// <summary>Creates an atlas metadata image with uniform confidence and packed flags.</summary>
    private static float[] CreateUniformMetaAtlas(float confidence, uint flags)
    {
        var data = new float[AtlasWidth * AtlasHeight * 2];
        float flagsF = FlagsToFloat(flags);
        for (int i = 0; i < AtlasWidth * AtlasHeight; i++)
        {
            int idx = i * 2;
            data[idx + 0] = confidence;
            data[idx + 1] = flagsF;
        }
        return data;
    }

    /// <summary>Writes radiance and encoded hit distance into one authored atlas texel.</summary>
    private static void SetAtlasTexel(float[] atlas, int x, int y, float r, float g, float b, float hitDistance)
    {
        int idx = (y * AtlasWidth + x) * 4;
        atlas[idx + 0] = r;
        atlas[idx + 1] = g;
        atlas[idx + 2] = b;
        atlas[idx + 3] = EncodeHitDistance(hitDistance);
    }

    /// <summary>Writes confidence and packed flags into one authored metadata texel.</summary>
    private static void SetMetaTexel(float[] meta, int x, int y, float confidence, uint flags)
    {
        int idx = (y * AtlasWidth + x) * 2;
        meta[idx + 0] = confidence;
        meta[idx + 1] = FlagsToFloat(flags);
    }

    /// <summary>Uses the filter's production parameter setters and frame binding.</summary>
    private void SetupUniforms(LumOnScreenProbeAtlasFilterShaderProgram programId, int filterRadius, float hitDistanceSigma)
    {
        UpdateAndBindLumOnFrameUbo(programId);
        programId.FilterRadius = filterRadius;
        programId.HitDistanceSigma = hitDistanceSigma;
    }

    /// <summary>Reads only the originally nominated atlas tuple without downloading neighboring probes.</summary>
    private static (float r, float g, float b, float a) ReadAtlasTexel(GpuTexture image, int x, int y)
    {
        var rgba=image.ReadPixelsRegion(x,y,1,1);
        int idx=0;
        return (rgba[idx + 0], rgba[idx + 1], rgba[idx + 2], rgba[idx + 3]);
    }
    #endregion
}
