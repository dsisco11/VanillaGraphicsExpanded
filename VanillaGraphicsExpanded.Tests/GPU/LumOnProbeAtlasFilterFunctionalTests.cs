using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

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
[Trait("Category", "GPU")]
public class LumOnProbeAtlasFilterFunctionalTests : LumOnShaderFunctionalTestBase
{
    public LumOnProbeAtlasFilterFunctionalTests(HeadlessGLFixture fixture) : base(fixture) { }

    private int CompileProbeAtlasFilterShader() => CompileShader("lumon_probe_atlas_filter.vsh", "lumon_probe_atlas_filter.fsh");

    private static float EncodeHitDistance(float distance) => (float)Math.Log(distance + 1.0);

    private static float FlagsToFloat(uint flags) => BitConverter.UInt32BitsToSingle(flags);

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

    private static void SetAnchorValidity(float[] anchorData, int probeX, int probeY, float valid)
    {
        int idx = (probeY * ProbeGridWidth + probeX) * 4 + 3;
        anchorData[idx] = valid;
    }

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

    private static void SetAtlasTexel(float[] atlas, int x, int y, float r, float g, float b, float hitDistance)
    {
        int idx = (y * AtlasWidth + x) * 4;
        atlas[idx + 0] = r;
        atlas[idx + 1] = g;
        atlas[idx + 2] = b;
        atlas[idx + 3] = EncodeHitDistance(hitDistance);
    }

    private static void SetMetaTexel(float[] meta, int x, int y, float confidence, uint flags)
    {
        int idx = (y * AtlasWidth + x) * 2;
        meta[idx + 0] = confidence;
        meta[idx + 1] = FlagsToFloat(flags);
    }

    private void SetupUniforms(int programId, int filterRadius, float hitDistanceSigma)
    {
        GL.UseProgram(programId);

        var gridSizeLoc = GL.GetUniformLocation(programId, "probeGridSize");
        GL.Uniform2(gridSizeLoc, (float)ProbeGridWidth, (float)ProbeGridHeight);

        var radiusLoc = GL.GetUniformLocation(programId, "filterRadius");
        GL.Uniform1(radiusLoc, filterRadius);

        var sigmaLoc = GL.GetUniformLocation(programId, "hitDistanceSigma");
        GL.Uniform1(sigmaLoc, hitDistanceSigma);

        // Samplers
        GL.Uniform1(GL.GetUniformLocation(programId, "octahedralAtlas"), 0);
        GL.Uniform1(GL.GetUniformLocation(programId, "probeAtlasMeta"), 1);
        GL.Uniform1(GL.GetUniformLocation(programId, "probeAnchorPosition"), 2);

        GL.UseProgram(0);
    }

    private static (float r, float g, float b, float a) ReadAtlasTexel(float[] rgba, int x, int y)
    {
        int idx = (y * AtlasWidth + x) * 4;
        return (rgba[idx + 0], rgba[idx + 1], rgba[idx + 2], rgba[idx + 3]);
    }

    [Fact]
    public void Filter_RespectsInvalidProbes()
    {
        EnsureShaderTestAvailable();

        // Hit flag (matches shader include: LUMON_META_HIT = 1u<<0)
        const uint HitFlag = 1u;

        var anchorPos = CreateValidProbeAnchors(1.0f);
        SetAnchorValidity(anchorPos, probeX: 0, probeY: 0, valid: 0.0f);

        var atlas = CreateUniformAtlas(1f, 0f, 0f, hitDistance: 10f);
        var meta = CreateUniformMetaAtlas(confidence: 1.0f, flags: HitFlag);

        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPos);
        using var atlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, atlas);
        using var metaTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rg32f, meta);

        using var outputAtlas = TestFramework.CreateTestGBuffer(
            AtlasWidth, AtlasHeight,
            PixelInternalFormat.Rgba16f,
            PixelInternalFormat.Rg32f);

        int programId = CompileProbeAtlasFilterShader();
        SetupUniforms(programId, filterRadius: 1, hitDistanceSigma: 1.0f);

        atlasTex.Bind(0);
        metaTex.Bind(1);
        anchorPosTex.Bind(2);

        TestFramework.RenderQuadTo(programId, outputAtlas);
        var outRgba = outputAtlas[0].ReadPixels();

        // Invalid probe (0,0) tile covers [0..7]x[0..7]
        var (r0, g0, b0, _) = ReadAtlasTexel(outRgba, 4, 4);
        Assert.True(r0 < 0.01f && g0 < 0.01f && b0 < 0.01f, "Invalid probe tile should output zero");

        // Valid probe (1,0) tile covers [8..15]x[0..7]
        var (r1, g1, b1, _) = ReadAtlasTexel(outRgba, 12, 4);
        Assert.True(r1 > 0.9f && g1 < 0.1f && b1 < 0.1f, "Valid probe tile should preserve non-zero radiance");

        GL.DeleteProgram(programId);
    }

    [Fact]
    public void Filter_RespectsConfidence()
    {
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

        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPos);
        using var atlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, atlas);
        using var metaTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rg32f, meta);

        using var outputAtlas = TestFramework.CreateTestGBuffer(
            AtlasWidth, AtlasHeight,
            PixelInternalFormat.Rgba16f,
            PixelInternalFormat.Rg32f);

        int programId = CompileProbeAtlasFilterShader();
        // Large sigma so hit distance does not reject; this isolates confidence weighting.
        SetupUniforms(programId, filterRadius: 1, hitDistanceSigma: 1000.0f);

        atlasTex.Bind(0);
        metaTex.Bind(1);
        anchorPosTex.Bind(2);

        TestFramework.RenderQuadTo(programId, outputAtlas);
        var outRgba = outputAtlas[0].ReadPixels();

        var (r, g, b, _) = ReadAtlasTexel(outRgba, 4, 4);
        Assert.True(r > 0.9f && g < 0.1f && b < 0.1f,
            $"Low-confidence neighbor should not bleed; got ({r:F3},{g:F3},{b:F3})");

        GL.DeleteProgram(programId);
    }

    [Fact]
    public void Filter_ReducesNoise_PreservesEdges()
    {
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

            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPos);
            using var atlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, atlas);
            using var metaTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rg32f, meta);

            using var outputAtlas = TestFramework.CreateTestGBuffer(
                AtlasWidth, AtlasHeight,
                PixelInternalFormat.Rgba16f,
                PixelInternalFormat.Rg32f);

            int programId = CompileProbeAtlasFilterShader();
            SetupUniforms(programId, filterRadius: 1, hitDistanceSigma: 1000.0f);

            atlasTex.Bind(0);
            metaTex.Bind(1);
            anchorPosTex.Bind(2);

            TestFramework.RenderQuadTo(programId, outputAtlas);
            var outRgba = outputAtlas[0].ReadPixels();

            var (r, _, b, _) = ReadAtlasTexel(outRgba, 4, 4);
            Assert.True(r > 0.3f && r < 0.7f && b > 0.3f && b < 0.7f,
                $"Expected smoothing toward a mix; got R={r:F3}, B={b:F3}");

            GL.DeleteProgram(programId);
        }

        // Case B: Edge preservation via hit-distance stopping
        {
            var atlas = CreateUniformAtlas(1f, 0f, 0f, hitDistance: 10f);
            var meta = CreateUniformMetaAtlas(confidence: 1.0f, flags: HitFlag);

            // Neighbor has a very different hit distance and different color.
            SetAtlasTexel(atlas, x: 5, y: 4, r: 0f, g: 1f, b: 0f, hitDistance: 200f);

            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPos);
            using var atlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, atlas);
            using var metaTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rg32f, meta);

            using var outputAtlas = TestFramework.CreateTestGBuffer(
                AtlasWidth, AtlasHeight,
                PixelInternalFormat.Rgba16f,
                PixelInternalFormat.Rg32f);

            int programId = CompileProbeAtlasFilterShader();
            // Small sigma => strong edge stop on large hit-distance delta.
            SetupUniforms(programId, filterRadius: 1, hitDistanceSigma: 0.05f);

            atlasTex.Bind(0);
            metaTex.Bind(1);
            anchorPosTex.Bind(2);

            TestFramework.RenderQuadTo(programId, outputAtlas);
            var outRgba = outputAtlas[0].ReadPixels();

            var (r, g, b, _) = ReadAtlasTexel(outRgba, 4, 4);
            Assert.True(r > 0.9f && g < 0.1f && b < 0.1f,
                $"Expected edge-stopped result to remain near center; got ({r:F3},{g:F3},{b:F3})");

            GL.DeleteProgram(programId);
        }
    }
}
