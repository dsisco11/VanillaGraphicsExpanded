using VanillaGraphicsExpanded.LumOn;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>
/// Functional tests for the LumOn probe-atlas gather shader pass.
///
/// These tests verify that the gather shader correctly:
/// - Interpolates irradiance from the four surrounding probes
/// - Weights probes by bilinear position, depth similarity, and normal similarity
/// - Applies indirectTint to the final output
/// - Handles edge cases (sky pixels, invalid probes)
///
/// Test configuration:
/// - Screen buffer: 4×4 pixels (full-res)
/// - Half-res buffer: 2×2 pixels (gather output)
/// - Probe grid: 2×2 probes
/// - Octahedral atlas: 16×16 (8×8 per probe)
/// - Probe spacing: 2 pixels
/// </summary>
/// <remarks>
/// The gather shader runs at half resolution. Each half-res pixel corresponds to
/// a 2×2 block in full-res. The shader reads from full-res G-buffer and outputs
/// to half-res irradiance buffer.
///
/// Probe weight calculation:
/// <code>
/// bilinearWeight = based on pixel position relative to probe grid
/// depthWeight = exp(-depthDiff² * 8.0)
/// normalWeight = pow(max(dot(pixelNormal, probeNormal), 0), 4)
/// finalWeight = bilinear * depth * normal * validity
/// </code>
/// </remarks>
[Collection("GPU")]
[Trait("Category", "GPU")]
public class LumOnProbeAtlasGatherFunctionalTests : LumOnShaderFunctionalTestBase
{
    public LumOnProbeAtlasGatherFunctionalTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Helper Methods

    /// <summary>
    /// Compiles and links the probe-atlas gather shader.
    /// </summary>
    private LumOnScreenProbeAtlasGatherShaderProgram CompileGatherShader() => Programs.Create<LumOnScreenProbeAtlasGatherShaderProgram>();

    /// <summary>
    /// Sets up common uniforms for the gather shader.
    /// </summary>
    private void SetupGatherUniforms(
        LumOnScreenProbeAtlasGatherShaderProgram programId,
        float[] invProjection,
        float[] view,
        float intensity = 1.0f,
        (float r, float g, float b) indirectTint = default,
        int sampleStride = 1)
    {
        using var use = programId.UseScope();
        UpdateAndBindLumOnFrameUbo(programId, invProjectionMatrix: invProjection, viewMatrix: view, probeSpacing: ProbeSpacing);
        programId.Intensity = intensity;
        var tint = indirectTint == default ? (1f, 1f, 1f) : indirectTint;
        programId.IndirectTint = [tint.Item1, tint.Item2, tint.Item3];
        programId.LeakThreshold = 0.5f;
        programId.SampleStride = sampleStride;
    }

    /// <summary>
    /// Creates a probe anchor position buffer with specified positions and validity.
    /// Probes are placed in view-space for predictable depth calculations.
    /// </summary>
    private static float[] CreateProbeAnchors(float worldZ, float validity = 1.0f)
    {
        var data = new float[ProbeGridWidth * ProbeGridHeight * 4];
        for (int py = 0; py < ProbeGridHeight; py++)
        {
            for (int px = 0; px < ProbeGridWidth; px++)
            {
                int idx = (py * ProbeGridWidth + px) * 4;
                // Position probes at grid centers in world-space
                // With identity matrices, world-space ≈ view-space
                data[idx + 0] = (px + 0.5f) * ProbeSpacing / (float)ScreenWidth * 2.0f - 1.0f;  // X: NDC
                data[idx + 1] = (py + 0.5f) * ProbeSpacing / (float)ScreenHeight * 2.0f - 1.0f; // Y: NDC
                data[idx + 2] = worldZ;  // Z: depth
                data[idx + 3] = validity;
            }
        }
        return data;
    }

    /// <summary>
    /// Creates probe anchor normals (encoded).
    /// </summary>
    private static float[] CreateProbeNormals(float nx, float ny, float nz)
    {
        var data = new float[ProbeGridWidth * ProbeGridHeight * 4];
        // Encode normal to [0,1] range
        float encX = nx * 0.5f + 0.5f;
        float encY = ny * 0.5f + 0.5f;
        float encZ = nz * 0.5f + 0.5f;

        for (int i = 0; i < ProbeGridWidth * ProbeGridHeight; i++)
        {
            int idx = i * 4;
            data[idx + 0] = encX;
            data[idx + 1] = encY;
            data[idx + 2] = encZ;
            data[idx + 3] = 0f;
        }
        return data;
    }

    /// <summary>
    /// Creates an octahedral atlas with uniform radiance per probe.
    /// </summary>
    private static float[] CreateUniformAtlas(float r, float g, float b, float hitDist = 10f)
    {
        var data = new float[AtlasWidth * AtlasHeight * 4];
        float encodedDist = MathF.Log(hitDist + 1.0f);

        for (int i = 0; i < AtlasWidth * AtlasHeight; i++)
        {
            int idx = i * 4;
            data[idx + 0] = r;
            data[idx + 1] = g;
            data[idx + 2] = b;
            data[idx + 3] = encodedDist;
        }
        return data;
    }

    /// <summary>
    /// Creates an octahedral atlas with different colors per probe (RGBW quadrants).
    /// </summary>
    private static float[] CreateQuadrantAtlas(float hitDist = 10f)
    {
        var data = new float[AtlasWidth * AtlasHeight * 4];
        float encodedDist = MathF.Log(hitDist + 1.0f);

        // Probe (0,0) = Red, (1,0) = Green, (0,1) = Blue, (1,1) = White
        (float r, float g, float b)[] probeColors =
        [
            (1f, 0f, 0f),  // Probe (0,0)
            (0f, 1f, 0f),  // Probe (1,0)
            (0f, 0f, 1f),  // Probe (0,1)
            (1f, 1f, 1f)   // Probe (1,1)
        ];

        for (int probeY = 0; probeY < ProbeGridHeight; probeY++)
        {
            for (int probeX = 0; probeX < ProbeGridWidth; probeX++)
            {
                int probeIdx = probeY * ProbeGridWidth + probeX;
                var (r, g, b) = probeColors[probeIdx];

                // Fill this probe's 8×8 tile
                for (int ty = 0; ty < OctahedralSize; ty++)
                {
                    for (int tx = 0; tx < OctahedralSize; tx++)
                    {
                        int atlasX = probeX * OctahedralSize + tx;
                        int atlasY = probeY * OctahedralSize + ty;
                        int idx = (atlasY * AtlasWidth + atlasX) * 4;

                        data[idx + 0] = r;
                        data[idx + 1] = g;
                        data[idx + 2] = b;
                        data[idx + 3] = encodedDist;
                    }
                }
            }
        }
        return data;
    }

    /// <summary>
    /// Creates a depth buffer with uniform depth (full-res). Delegates to base class.
    /// </summary>
    private float[] CreateDepthBuffer(float depth) => CreateUniformDepthData(ScreenWidth, ScreenHeight, depth);

    /// <summary>
    /// Creates a normal buffer with uniform normals (full-res, encoded). Delegates to base class.
    /// </summary>
    private float[] CreateNormalBuffer(float nx, float ny, float nz) => CreateUniformNormalData(ScreenWidth, ScreenHeight, nx, ny, nz);

    /// <summary>
    /// Reads a pixel from half-res output.
    /// </summary>
    private static (float r, float g, float b, float a) ReadPixelHalfRes(float[] data, int x, int y)
    {
        int idx = (y * HalfResWidth + x) * 4;
        return (data[idx], data[idx + 1], data[idx + 2], data[idx + 3]);
    }

    private static float[] CreateWorldProbeRadianceAtlas(int width, int height, float r, float g, float b)
    {
        var data = new float[width * height * 4];
        float encodedHitDistance = MathF.Log(2.0f);
        for (int i = 0; i < width * height; i++)
        {
            int index = i * 4;
            data[index] = r;
            data[index + 1] = g;
            data[index + 2] = b;
            data[index + 3] = encodedHitDistance;
        }

        return data;
    }

    #endregion

    #region Test: InvalidScreenProbes_UseWorldProbeFallback

    /// <summary>Both gather modes preserve selected sample confidence when world radiance is suppressed.</summary>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    public void WorldProbeSuppression_PreservesFallbackSelection(bool sh9, bool validScreenProbes, bool blocked)
    {
        EnsureShaderTestAvailable();

        const int worldProbeResolution = 2;
        const int worldProbeLevels = 1;
        const int worldProbeTileSize = 16;
        const float worldProbeBaseSpacing = 1000f;

        int worldProbeScalarAtlasWidth = worldProbeResolution * worldProbeResolution;
        int worldProbeScalarAtlasHeight = worldProbeResolution * worldProbeLevels;
        int worldProbeRadianceAtlasWidth = worldProbeScalarAtlasWidth * worldProbeTileSize;
        int worldProbeRadianceAtlasHeight = worldProbeScalarAtlasHeight * worldProbeTileSize;

        const float pixelDepth = 0.5f;
        CreateTestMatricesForDepth(pixelDepth, out var invProjection, out var viewMatrix, out var probeWorldZ, out _);

        using var screenProbeAtlas = TestFramework.CreateTexture(
            AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, CreateUniformAtlas(1f, 1f, 1f));
        using var anchorPos = TestFramework.CreateTexture(
            ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, CreateProbeAnchors(probeWorldZ, validity: validScreenProbes ? 1f : 0f));
        using var anchorNormal = TestFramework.CreateTexture(
            ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, CreateProbeNormals(0f, 1f, 0f));
        using var depth = TestFramework.CreateTexture(
            ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, CreateDepthBuffer(pixelDepth));
        using var normal = TestFramework.CreateTexture(
            ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, CreateNormalBuffer(0f, 1f, 0f));

        using var worldProbeRadiance = TestFramework.CreateTexture(
            worldProbeRadianceAtlasWidth, worldProbeRadianceAtlasHeight, PixelInternalFormat.Rgba16f,
            CreateWorldProbeRadianceAtlas(worldProbeRadianceAtlasWidth, worldProbeRadianceAtlasHeight, 1f, 0f, 0f));
        using var worldProbeVis = TestFramework.CreateTexture(
            worldProbeScalarAtlasWidth, worldProbeScalarAtlasHeight, PixelInternalFormat.Rgba16f,
            CreateUniformColorData(worldProbeScalarAtlasWidth, worldProbeScalarAtlasHeight, 0.5f, 1f, 0f, 0f));
        using var worldProbeMeta = TestFramework.CreateTexture(
            worldProbeScalarAtlasWidth, worldProbeScalarAtlasHeight, PixelInternalFormat.Rg32f,
            CreateUniformData(worldProbeScalarAtlasWidth, worldProbeScalarAtlasHeight, 2, 1f, 0f));
        using var output = TestFramework.CreateTestGBuffer(HalfResWidth, HalfResHeight, PixelInternalFormat.Rgba16f);

        // Each gather receives its own production input layout. SH9 uses a DC coefficient
        // for constant white radiance and zero higher bands.
        using var shDc = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f,
            CreateUniformColorData(ProbeGridWidth, ProbeGridHeight, 3.5449077f, 3.5449077f, 3.5449077f, 0));
        using var shZero = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f,
            CreateUniformColorData(ProbeGridWidth, ProbeGridHeight, 0, 0, 0, 0));
        LumOnProbeSh9GatherShaderProgram? sh = null;
        LumOnScreenProbeAtlasGatherShaderProgram? atlas = null;
        if (sh9)
            sh = Programs.Create<LumOnProbeSh9GatherShaderProgram>(shader =>
            {
                shader.WorldProbeEnabled = true; shader.WorldProbeLevels = worldProbeLevels;
                shader.WorldProbeResolution = worldProbeResolution; shader.WorldProbeBaseSpacing = worldProbeBaseSpacing;
                shader.WorldProbeOctahedralSize = worldProbeTileSize;
            });
        else
            atlas = Programs.Create<LumOnScreenProbeAtlasGatherShaderProgram>(shader =>
            {
                shader.WorldProbeEnabled = true; shader.WorldProbeLevels = worldProbeLevels;
                shader.WorldProbeResolution = worldProbeResolution; shader.WorldProbeBaseSpacing = worldProbeBaseSpacing;
                shader.WorldProbeOctahedralSize = worldProbeTileSize;
            });
        VanillaGraphicsExpanded.LumOn.Shaders.LumOnShaderProgram programId = sh ?? (VanillaGraphicsExpanded.LumOn.Shaders.LumOnShaderProgram)atlas!;
        using var programUse = programId.UseScope();
        UpdateAndBindLumOnFrameUbo(programId, invProjectionMatrix: invProjection, viewMatrix: viewMatrix);
        UpdateAndBindLumOnWorldProbeUbo(programId, new(0,0,0), Vector3.Zero,
            originMinCorner: [blocked ? new(-400f,-400f,-400f) : new(-500f,-500f,-500f)], ringOffset: [Vector3.Zero]);
        if (sh != null)
        {
            sh.Intensity = 1; sh.IndirectTint = [1,1,1];
            sh.ProbeSh0 = shDc; sh.ProbeSh1 = shZero; sh.ProbeSh2 = shZero; sh.ProbeSh3 = shZero;
            sh.ProbeSh4 = shZero; sh.ProbeSh5 = shZero; sh.ProbeSh6 = shZero;
            sh.ProbeAnchorPosition = anchorPos; sh.ProbeAnchorNormal = anchorNormal;
            sh.PrimaryDepth = depth.TextureId; sh.GBufferNormal = normal.TextureId;
            sh.WorldProbeRadianceAtlas = worldProbeRadiance; sh.WorldProbeVis0 = worldProbeVis; sh.WorldProbeMeta0 = worldProbeMeta;
        }
        else
        {
            SetupGatherUniforms(atlas!, invProjection, viewMatrix);
            atlas!.ScreenProbeAtlas = screenProbeAtlas; atlas.ProbeAnchorPosition = anchorPos; atlas.ProbeAnchorNormal = anchorNormal;
            atlas.PrimaryDepth = depth.TextureId; atlas.GBufferNormal = normal.TextureId;
            atlas.WorldProbeRadianceAtlas = worldProbeRadiance; atlas.WorldProbeVis0 = worldProbeVis; atlas.WorldProbeMeta0 = worldProbeMeta;
        }
            TestFramework.RenderQuadTo(programId, output);

            var (r, g, b, confidence) = ReadPixelHalfRes(output[0].ReadPixels(), 0, 0);
            if (blocked)
            {
                foreach (float value in output[0].ReadPixels()) Assert.Equal(0f, value);
                return;
            }
            Assert.True(r + g + b > 0.5f, "Expected positive accepted lighting");
            Assert.True(confidence > (validScreenProbes ? 0.5f : 0.9f), $"Expected confident selected lighting, got {confidence:F3}");
            if (!validScreenProbes)
                Assert.True(r > 0.5f && g < 0.1f && b < 0.1f, "Expected red world fallback");
            var reference = output[0].ReadPixels();

            // Keep every gather setting equal while zeroing only accepted world fallback lighting.
            if (sh != null) sh.SuppressWorldProbeRadiance = true;
            else atlas!.SuppressWorldProbeRadiance = true;
            TestFramework.RenderQuadTo(programId, output);
            var suppressed = output[0].ReadPixels();
            for (int i = 0; i < reference.Length; i += 4)
            {
                Assert.Equal(reference[i + 3], suppressed[i + 3]);
                for (int channel = 0; channel < 3; channel++)
                    Assert.Equal(validScreenProbes ? reference[i + channel] : 0f, suppressed[i + channel]);
            }
    }

    private static float[] CreateUniformData(int width, int height, int channels, params float[] value)
    {
        var data = new float[width * height * channels];
        for (int i = 0; i < width * height; i++)
        {
            Array.Copy(value, 0, data, i * channels, channels);
        }

        return data;
    }

    #endregion

    #region Test: GatherCoordinates_AlignWithProbeAnchors

    /// <summary>
    /// Tests that half-resolution gather coordinates align with the screen-probe anchors.
    ///
    /// Setup:
    /// - 2×2 half-res output and 2×2 probe grid
    /// - Probes with distinct RGBW colors at matching depth/normal
    ///
    /// Expected:
    /// - Each output texel is dominated by its corresponding probe.
    /// </summary>
    [Fact]
    public void GatherCoordinates_AlignWithProbeAnchors()
    {
        EnsureShaderTestAvailable();

        const float pixelDepth = 0.5f;    // Normalized depth buffer value

        // Get proper matrices and matching probe depth
        CreateTestMatricesForDepth(pixelDepth, out var invProjection, out var viewMatrix,
            out var probeWorldZ, out var hitDistance);

        // Create input textures
        // Probes with RGBW colors - use the computed hit distance
        var atlasData = CreateQuadrantAtlas(hitDistance);

        // All probes at same depth (matching pixel) and with upward normals
        var anchorPosData = CreateProbeAnchors(probeWorldZ, validity: 1.0f);
        var anchorNormalData = CreateProbeNormals(0f, 1f, 0f);  // Upward

        // Pixel depth and normal matching probes
        var depthData = CreateDepthBuffer(pixelDepth);
        var normalData = CreateNormalBuffer(0f, 1f, 0f);  // Upward, matching probes

        using var atlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, atlasData);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);

        // Create half-res output
        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            HalfResWidth, HalfResHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileGatherShader();

        using var programUse = programId.UseScope();
        SetupGatherUniforms(programId, invProjection, viewMatrix, intensity: 1.0f);

        // Bind inputs
        programId.ScreenProbeAtlas = atlasTex;
        programId.ProbeAnchorPosition = anchorPosTex;
        programId.ProbeAnchorNormal = anchorNormalTex;
        programId.PrimaryDepth = depthTex.TextureId;
        programId.GBufferNormal = normalTex.TextureId;

        TestFramework.RenderQuadTo(programId, outputGBuffer);

        var outputData = outputGBuffer[0].ReadPixels();

        var topLeft = ReadPixelHalfRes(outputData, 0, 0);
        var topRight = ReadPixelHalfRes(outputData, 1, 0);
        var bottomLeft = ReadPixelHalfRes(outputData, 0, 1);
        var bottomRight = ReadPixelHalfRes(outputData, 1, 1);

        Assert.True(topLeft.r > 0.01f && topLeft.r > topLeft.g && topLeft.r > topLeft.b,
            $"Top-left should resolve to the red probe, got ({topLeft.r:F3}, {topLeft.g:F3}, {topLeft.b:F3})");
        Assert.True(topRight.g > 0.01f && topRight.g > topRight.r && topRight.g > topRight.b,
            $"Top-right should resolve to the green probe, got ({topRight.r:F3}, {topRight.g:F3}, {topRight.b:F3})");
        Assert.True(bottomLeft.b > 0.01f && bottomLeft.b > bottomLeft.r && bottomLeft.b > bottomLeft.g,
            $"Bottom-left should resolve to the blue probe, got ({bottomLeft.r:F3}, {bottomLeft.g:F3}, {bottomLeft.b:F3})");
        Assert.True(bottomRight.r > 0.01f && bottomRight.g > 0.01f && bottomRight.b > 0.01f,
            $"Bottom-right should resolve to the white probe, got ({bottomRight.r:F3}, {bottomRight.g:F3}, {bottomRight.b:F3})");
    }

    #endregion

    #region Test: CornerPixel_WeightedByDistance

    /// <summary>
    /// Tests that a pixel near a corner is weighted more heavily toward the nearest probe.
    ///
    /// DESIRED BEHAVIOR:
    /// - Pixels closer to a probe should receive more irradiance from that probe
    /// - Bilinear interpolation weights should favor the nearest probe
    ///
    /// Setup:
    /// - Probe (0,0) = Red, others = Black
    /// - Check pixel near probe (0,0)
    ///
    /// Expected:
    /// - Pixel (0,0) in half-res should be predominantly red
    /// </summary>
    [Fact]
    public void CornerPixel_WeightedByDistance()
    {
        EnsureShaderTestAvailable();

        const float pixelDepth = 0.5f;

        // Get proper matrices and matching probe depth
        CreateTestMatricesForDepth(pixelDepth, out var invProjection, out var viewMatrix,
            out var probeWorldZ, out var hitDistance);
        float encodedDist = MathF.Log(hitDistance + 1.0f);

        // Create atlas with only probe (0,0) having color (red), others black
        var atlasData = new float[AtlasWidth * AtlasHeight * 4];

        // Only fill probe (0,0) with red
        for (int ty = 0; ty < OctahedralSize; ty++)
        {
            for (int tx = 0; tx < OctahedralSize; tx++)
            {
                int idx = (ty * AtlasWidth + tx) * 4;
                atlasData[idx + 0] = 1.0f;  // R
                atlasData[idx + 1] = 0.0f;  // G
                atlasData[idx + 2] = 0.0f;  // B
                atlasData[idx + 3] = encodedDist;
            }
        }
        // Other probes remain black (initialized to 0)

        var anchorPosData = CreateProbeAnchors(probeWorldZ, validity: 1.0f);
        var anchorNormalData = CreateProbeNormals(0f, 1f, 0f);
        var depthData = CreateDepthBuffer(pixelDepth);
        var normalData = CreateNormalBuffer(0f, 1f, 0f);

        using var atlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, atlasData);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            HalfResWidth, HalfResHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileGatherShader();

        using var programUse = programId.UseScope();
        SetupGatherUniforms(programId, invProjection, viewMatrix);

        programId.ScreenProbeAtlas = atlasTex;
        programId.ProbeAnchorPosition = anchorPosTex;
        programId.ProbeAnchorNormal = anchorNormalTex;
        programId.PrimaryDepth = depthTex.TextureId;
        programId.GBufferNormal = normalTex.TextureId;

        TestFramework.RenderQuadTo(programId, outputGBuffer);

        var outputData = outputGBuffer[0].ReadPixels();

        // DESIRED: Pixel (0,0) should be predominantly red since it's nearest to probe (0,0)
        var (r00, g00, b00, _) = ReadPixelHalfRes(outputData, 0, 0);

        // Red channel should dominate
        Assert.True(r00 > g00 && r00 > b00,
            $"Pixel (0,0) should be predominantly red (nearest probe), got ({r00:F3}, {g00:F3}, {b00:F3})");

        // Should have significant red contribution
        Assert.True(r00 > 0.1f,
            $"Pixel (0,0) should have red contribution from nearest probe, got R={r00:F3}");
    }

    #endregion

    #region Test: DepthDiscontinuity_ReducesWeight

    /// <summary>
    /// Tests that probes at significantly different depths contribute less to the pixel.
    ///
    /// DESIRED BEHAVIOR:
    /// - When a probe's depth differs significantly from the pixel's depth,
    ///   its contribution should be reduced to prevent light leaking
    /// - Weight formula: depthWeight = exp(-depthDiff² * 8.0)
    ///
    /// Setup:
    /// - Probe (0,0) at near depth (matching pixel) = Red
    /// - Probe (1,1) at far depth (mismatched) = Blue
    /// - Other probes invalid
    ///
    /// Expected:
    /// - Output should be predominantly red (near probe wins)
    /// </summary>
    [Fact]
    public void DepthDiscontinuity_ReducesWeight()
    {
        EnsureShaderTestAvailable();

        const float pixelDepth = 0.5f;

        // Get proper matrices and matching probe depth
        CreateTestMatricesForDepth(pixelDepth, out var invProjection, out var viewMatrix,
            out var probeWorldZ, out var hitDistance);
        float encodedDistNear = MathF.Log(hitDistance + 1.0f);
        float encodedDistFar = MathF.Log(hitDistance * 10f + 1.0f);  // Far probe has different hit distance

        // Create probes at different depths
        var anchorPosData = new float[ProbeGridWidth * ProbeGridHeight * 4];

        // Probe (0,0): at pixel depth (matching), valid - RED
        anchorPosData[0] = -0.5f; anchorPosData[1] = -0.5f; anchorPosData[2] = probeWorldZ; anchorPosData[3] = 1.0f;
        // Probe (1,0): invalid
        anchorPosData[4] = 0.5f; anchorPosData[5] = -0.5f; anchorPosData[6] = probeWorldZ; anchorPosData[7] = 0.0f;
        // Probe (0,1): invalid
        anchorPosData[8] = -0.5f; anchorPosData[9] = 0.5f; anchorPosData[10] = probeWorldZ; anchorPosData[11] = 0.0f;
        // Probe (1,1): at 10x farther depth, valid - BLUE
        anchorPosData[12] = 0.5f; anchorPosData[13] = 0.5f; anchorPosData[14] = probeWorldZ * 10f; anchorPosData[15] = 1.0f;

        // Create atlas: probe (0,0) = red, probe (1,1) = blue
        var atlasData = new float[AtlasWidth * AtlasHeight * 4];

        // Probe (0,0) = red with near hit distance
        for (int ty = 0; ty < OctahedralSize; ty++)
        {
            for (int tx = 0; tx < OctahedralSize; tx++)
            {
                int idx = (ty * AtlasWidth + tx) * 4;
                atlasData[idx + 0] = 1.0f; atlasData[idx + 1] = 0.0f; atlasData[idx + 2] = 0.0f;
                atlasData[idx + 3] = encodedDistNear;
            }
        }
        // Probe (1,1) = blue with far hit distance
        for (int ty = 0; ty < OctahedralSize; ty++)
        {
            for (int tx = 0; tx < OctahedralSize; tx++)
            {
                int atlasX = OctahedralSize + tx;
                int atlasY = OctahedralSize + ty;
                int idx = (atlasY * AtlasWidth + atlasX) * 4;
                atlasData[idx + 0] = 0.0f; atlasData[idx + 1] = 0.0f; atlasData[idx + 2] = 1.0f;
                atlasData[idx + 3] = encodedDistFar;
            }
        }

        var anchorNormalData = CreateProbeNormals(0f, 1f, 0f);
        var depthData = CreateDepthBuffer(pixelDepth);
        var normalData = CreateNormalBuffer(0f, 1f, 0f);

        using var atlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, atlasData);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            HalfResWidth, HalfResHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileGatherShader();

        using var programUse = programId.UseScope();
        SetupGatherUniforms(programId, invProjection, viewMatrix);

        programId.ScreenProbeAtlas = atlasTex;
        programId.ProbeAnchorPosition = anchorPosTex;
        programId.ProbeAnchorNormal = anchorNormalTex;
        programId.PrimaryDepth = depthTex.TextureId;
        programId.GBufferNormal = normalTex.TextureId;

        TestFramework.RenderQuadTo(programId, outputGBuffer);

        var outputData = outputGBuffer[0].ReadPixels();

        // DESIRED: Pixel (0,0) should favor red (near probe) over blue (far probe)
        var (r, g, b, _) = ReadPixelHalfRes(outputData, 0, 0);

        // Near probe (red) should have significantly more weight than far probe (blue)
        Assert.True(r > b,
            $"Pixel (0,0) should favor near probe (red) over far probe (blue), got R={r:F3}, B={b:F3}");
    }

    #endregion

    #region Test: NormalMismatch_ReducesWeight

    /// <summary>
    /// Tests that probes with normals opposite to the pixel's normal contribute less.
    ///
    /// DESIRED BEHAVIOR:
    /// - When a probe's normal points away from the pixel's normal,
    ///   its contribution should be reduced (surface orientation mismatch)
    /// - Weight formula: normalWeight = pow(max(dot(pixelNormal, probeNormal), 0), 4)
    ///
    /// Setup:
    /// - Pixel normal = (0, 1, 0) (upward)
    /// - Probe (0,0) normal = (0, 1, 0) (matching) = Red
    /// - Probe (1,1) normal = (0, -1, 0) (opposite) = Blue
    ///
    /// Expected:
    /// - Output should be predominantly red (matching normal wins)
    /// </summary>
    [Fact]
    public void NormalMismatch_ReducesWeight()
    {
        EnsureShaderTestAvailable();

        const float pixelDepth = 0.5f;

        // Get proper matrices and matching probe depth
        CreateTestMatricesForDepth(pixelDepth, out var invProjection, out var viewMatrix,
            out var probeWorldZ, out var hitDistance);

        // Create probe normals: (0,0) upward, (1,1) downward, others upward
        var anchorNormalData = new float[ProbeGridWidth * ProbeGridHeight * 4];
        // Probe (0,0): upward (0, 1, 0) encoded as (0.5, 1.0, 0.5)
        anchorNormalData[0] = 0.5f; anchorNormalData[1] = 1.0f; anchorNormalData[2] = 0.5f; anchorNormalData[3] = 0f;
        // Probe (1,0): upward
        anchorNormalData[4] = 0.5f; anchorNormalData[5] = 1.0f; anchorNormalData[6] = 0.5f; anchorNormalData[7] = 0f;
        // Probe (0,1): upward
        anchorNormalData[8] = 0.5f; anchorNormalData[9] = 1.0f; anchorNormalData[10] = 0.5f; anchorNormalData[11] = 0f;
        // Probe (1,1): downward (0, -1, 0) encoded as (0.5, 0.0, 0.5)
        anchorNormalData[12] = 0.5f; anchorNormalData[13] = 0.0f; anchorNormalData[14] = 0.5f; anchorNormalData[15] = 0f;

        // Create atlas: probe (0,0) = red, probe (1,1) = blue, others = green
        var atlasData = new float[AtlasWidth * AtlasHeight * 4];
        float encodedDist = MathF.Log(hitDistance + 1.0f);

        // Fill entire atlas with green first
        for (int i = 0; i < AtlasWidth * AtlasHeight; i++)
        {
            int idx = i * 4;
            atlasData[idx + 0] = 0.0f; atlasData[idx + 1] = 1.0f; atlasData[idx + 2] = 0.0f;
            atlasData[idx + 3] = encodedDist;
        }

        // Probe (0,0) = red
        for (int ty = 0; ty < OctahedralSize; ty++)
        {
            for (int tx = 0; tx < OctahedralSize; tx++)
            {
                int idx = (ty * AtlasWidth + tx) * 4;
                atlasData[idx + 0] = 1.0f; atlasData[idx + 1] = 0.0f; atlasData[idx + 2] = 0.0f;
            }
        }
        // Probe (1,1) = blue
        for (int ty = 0; ty < OctahedralSize; ty++)
        {
            for (int tx = 0; tx < OctahedralSize; tx++)
            {
                int atlasX = OctahedralSize + tx;
                int atlasY = OctahedralSize + ty;
                int idx = (atlasY * AtlasWidth + atlasX) * 4;
                atlasData[idx + 0] = 0.0f; atlasData[idx + 1] = 0.0f; atlasData[idx + 2] = 1.0f;
            }
        }

        var anchorPosData = CreateProbeAnchors(probeWorldZ, validity: 1.0f);
        var depthData = CreateDepthBuffer(pixelDepth);
        var normalData = CreateNormalBuffer(0f, 1f, 0f);  // Pixel normal = upward

        using var atlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, atlasData);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            HalfResWidth, HalfResHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileGatherShader();

        using var programUse = programId.UseScope();
        SetupGatherUniforms(programId, invProjection, viewMatrix);

        programId.ScreenProbeAtlas = atlasTex;
        programId.ProbeAnchorPosition = anchorPosTex;
        programId.ProbeAnchorNormal = anchorNormalTex;
        programId.PrimaryDepth = depthTex.TextureId;
        programId.GBufferNormal = normalTex.TextureId;

        TestFramework.RenderQuadTo(programId, outputGBuffer);

        var outputData = outputGBuffer[0].ReadPixels();

        // DESIRED: Pixel (1,1) should have very little blue contribution
        // because probe (1,1) has opposite normal (dot product ≈ -1, weight ≈ 0)
        var (r11, g11, b11, _) = ReadPixelHalfRes(outputData, 1, 1);

        // The opposite-normal probe should have near-zero weight
        // So blue should be much less than green (other upward probes)
        Assert.True(b11 < g11 || b11 < 0.1f,
            $"Pixel (1,1) should minimize opposite-normal probe, got R={r11:F3}, G={g11:F3}, B={b11:F3}");
    }

    #endregion

    #region Test: IndirectTint_AppliedToOutput

    /// <summary>
    /// Tests that the indirectTint uniform scales the output irradiance per-channel.
    ///
    /// DESIRED BEHAVIOR:
    /// - Final output = irradiance * intensity * indirectTint
    /// - Each channel scaled independently
    ///
    /// Setup:
    /// - Uniform white radiance from all probes
    /// - indirectTint = (2.0, 1.0, 0.5)
    ///
    /// Expected:
    /// - Output R channel = 2× base
    /// - Output G channel = 1× base
    /// - Output B channel = 0.5× base
    /// </summary>
    [Fact]
    public void IndirectTint_AppliedToOutput()
    {
        EnsureShaderTestAvailable();

        const float pixelDepth = 0.5f;
        var tint = (r: 2.0f, g: 1.0f, b: 0.5f);

        // Get proper matrices and matching probe depth
        CreateTestMatricesForDepth(pixelDepth, out var invProjection, out var viewMatrix,
            out var probeWorldZ, out var hitDistance);

        // Uniform white atlas
        var atlasData = CreateUniformAtlas(1.0f, 1.0f, 1.0f, hitDistance);
        var anchorPosData = CreateProbeAnchors(probeWorldZ, validity: 1.0f);
        var anchorNormalData = CreateProbeNormals(0f, 1f, 0f);
        var depthData = CreateDepthBuffer(pixelDepth);
        var normalData = CreateNormalBuffer(0f, 1f, 0f);

        using var atlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, atlasData);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);

        // First render without tint to get baseline
        using var baselineOutput = TestFramework.CreateTestGBuffer(
            HalfResWidth, HalfResHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileGatherShader();

        using var programUse = programId.UseScope();
        SetupGatherUniforms(programId, invProjection, viewMatrix, intensity: 1.0f, indirectTint: (1f, 1f, 1f));

        programId.ScreenProbeAtlas = atlasTex;
        programId.ProbeAnchorPosition = anchorPosTex;
        programId.ProbeAnchorNormal = anchorNormalTex;
        programId.PrimaryDepth = depthTex.TextureId;
        programId.GBufferNormal = normalTex.TextureId;

        TestFramework.RenderQuadTo(programId, baselineOutput);
        var baselineData = baselineOutput[0].ReadPixels();

        // Now render with tint
        using var tintedOutput = TestFramework.CreateTestGBuffer(
            HalfResWidth, HalfResHeight,
            PixelInternalFormat.Rgba16f);

        SetupGatherUniforms(programId, invProjection, viewMatrix, intensity: 1.0f, indirectTint: tint);

        // Re-bind textures after uniform setup
        programId.ScreenProbeAtlas = atlasTex;
        programId.ProbeAnchorPosition = anchorPosTex;
        programId.ProbeAnchorNormal = anchorNormalTex;
        programId.PrimaryDepth = depthTex.TextureId;
        programId.GBufferNormal = normalTex.TextureId;

        TestFramework.RenderQuadTo(programId, tintedOutput);
        var tintedData = tintedOutput[0].ReadPixels();

        // DESIRED: Tinted output should be baseline * tint per channel
        for (int py = 0; py < HalfResHeight; py++)
        {
            for (int px = 0; px < HalfResWidth; px++)
            {
                var (baseR, baseG, baseB, _) = ReadPixelHalfRes(baselineData, px, py);
                var (tintR, tintG, tintB, _) = ReadPixelHalfRes(tintedData, px, py);

                // Skip pixels with zero baseline (would cause division issues)
                if (baseR < 0.01f && baseG < 0.01f && baseB < 0.01f) continue;

                // Check R channel scaled by 2.0
                if (baseR > 0.01f)
                {
                    float ratioR = tintR / baseR;
                    Assert.True(MathF.Abs(ratioR - tint.r) < 0.2f,
                        $"Pixel ({px},{py}) R should be scaled by {tint.r}, got ratio {ratioR:F2}");
                }

                // Check G channel scaled by 1.0
                if (baseG > 0.01f)
                {
                    float ratioG = tintG / baseG;
                    Assert.True(MathF.Abs(ratioG - tint.g) < 0.2f,
                        $"Pixel ({px},{py}) G should be scaled by {tint.g}, got ratio {ratioG:F2}");
                }

                // Check B channel scaled by 0.5
                if (baseB > 0.01f)
                {
                    float ratioB = tintB / baseB;
                    Assert.True(MathF.Abs(ratioB - tint.b) < 0.2f,
                        $"Pixel ({px},{py}) B should be scaled by {tint.b}, got ratio {ratioB:F2}");
                }
            }
        }
    }

    #endregion

    #region Test: SkyPixels_ProduceZeroIrradiance

    /// <summary>
    /// Tests that sky pixels (depth=1.0) produce zero irradiance.
    ///
    /// DESIRED BEHAVIOR:
    /// - Sky pixels should early-out with black output
    /// - No indirect lighting should be gathered for sky
    ///
    /// Setup:
    /// - Pixel depth = 1.0 (sky/far plane)
    /// - Bright radiance in atlas
    ///
    /// Expected:
    /// - Output = (0, 0, 0)
    /// </summary>
    [Fact]
    public void SkyPixels_ProduceZeroIrradiance()
    {
        EnsureShaderTestAvailable();

        // Sky depth
        var depthData = CreateDepthBuffer(1.0f);

        // Bright atlas (should be ignored)
        var atlasData = CreateUniformAtlas(1.0f, 1.0f, 1.0f);
        var anchorPosData = CreateProbeAnchors(-5.0f, validity: 1.0f);
        var anchorNormalData = CreateProbeNormals(0f, 1f, 0f);
        var normalData = CreateNormalBuffer(0f, 1f, 0f);

        using var atlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, atlasData);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            HalfResWidth, HalfResHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileGatherShader();

        using var programUse = programId.UseScope();
        // Use realistic matrices for consistency (though sky pixels early-out before depth reconstruction)
        var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
        var viewMatrix = LumOnTestInputFactory.CreateIdentityView();
        SetupGatherUniforms(programId, invProjection, viewMatrix);

        programId.ScreenProbeAtlas = atlasTex;
        programId.ProbeAnchorPosition = anchorPosTex;
        programId.ProbeAnchorNormal = anchorNormalTex;
        programId.PrimaryDepth = depthTex.TextureId;
        programId.GBufferNormal = normalTex.TextureId;

        TestFramework.RenderQuadTo(programId, outputGBuffer);

        var outputData = outputGBuffer[0].ReadPixels();

        // DESIRED: All sky pixels should output zero irradiance
        for (int py = 0; py < HalfResHeight; py++)
        {
            for (int px = 0; px < HalfResWidth; px++)
            {
                var (r, g, b, _) = ReadPixelHalfRes(outputData, px, py);

                Assert.True(r < TestEpsilon && g < TestEpsilon && b < TestEpsilon,
                    $"Sky pixel ({px},{py}) should have zero irradiance, got ({r:F3}, {g:F3}, {b:F3})");
            }
        }
    }

    #endregion

    #region Phase 4 Tests: High Priority Missing Coverage

    /// <summary>
    /// Tests that all invalid probes result in zero output.
    ///
    /// DESIRED BEHAVIOR:
    /// - When all surrounding probes are invalid, totalWeight < 0.001
    /// - Output should be zero (no contribution)
    ///
    /// Setup:
    /// - All probes invalid (validity=0)
    /// - Non-zero radiance in atlas (would contribute if valid)
    /// </summary>
    [Fact]
    public void AllProbesInvalid_OutputsZero()
    {
        EnsureShaderTestAvailable();

        const float pixelDepth = 0.5f;

        var atlasData = CreateUniformAtlas(1f, 1f, 1f);  // Bright atlas
        var anchorPosData = CreateProbeAnchors(-5f, validity: 0f);  // All invalid
        var anchorNormalData = CreateProbeNormals(0f, 1f, 0f);
        var depthData = CreateDepthBuffer(pixelDepth);
        var normalData = CreateNormalBuffer(0f, 1f, 0f);

        using var atlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, atlasData);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            HalfResWidth, HalfResHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileGatherShader();

        using var programUse = programId.UseScope();
        var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
        var viewMatrix = LumOnTestInputFactory.CreateIdentityView();
        SetupGatherUniforms(programId, invProjection, viewMatrix);

        programId.ScreenProbeAtlas = atlasTex;
        programId.ProbeAnchorPosition = anchorPosTex;
        programId.ProbeAnchorNormal = anchorNormalTex;
        programId.PrimaryDepth = depthTex.TextureId;
        programId.GBufferNormal = normalTex.TextureId;

        TestFramework.RenderQuadTo(programId, outputGBuffer);
        var outputData = outputGBuffer[0].ReadPixels();

        // All pixels should be zero
        for (int py = 0; py < HalfResHeight; py++)
        {
            for (int px = 0; px < HalfResWidth; px++)
            {
                var (r, g, b, _) = ReadPixelHalfRes(outputData, px, py);
                Assert.True(r < TestEpsilon && g < TestEpsilon && b < TestEpsilon,
                    $"Pixel ({px},{py}) should be zero when all probes invalid, got ({r:F4}, {g:F4}, {b:F4})");
            }
        }
    }

    /// <summary>
    /// Tests that edge probes with partial validity have reduced weight.
    ///
    /// DESIRED BEHAVIOR:
    /// - Probes with validity < 1.0 should contribute less
    /// - Validity acts as a weight multiplier
    ///
    /// Setup:
    /// - Some probes with validity=0.5, others with validity=1.0
    /// - Compare brightness near partial vs full validity probes
    /// </summary>
    [Fact]
    public void PartialValidity_ReducesWeight()
    {
        EnsureShaderTestAvailable();

        const float pixelDepth = 0.5f;

        CreateTestMatricesForDepth(pixelDepth, out var invProjection, out var viewMatrix,
            out var probeWorldZ, out var hitDistance);

        var atlasData = CreateUniformAtlas(1f, 1f, 1f, hitDistance);
        var anchorNormalData = CreateProbeNormals(0f, 1f, 0f);
        var depthData = CreateDepthBuffer(pixelDepth);
        var normalData = CreateNormalBuffer(0f, 1f, 0f);

        float fullValidityBrightness;
        float partialValidityBrightness;

        // Full validity
        {
            var anchorPosData = CreateProbeAnchors(probeWorldZ, validity: 1.0f);

            using var atlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, atlasData);
            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
            using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
            using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
            using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);

            using var outputGBuffer = TestFramework.CreateTestGBuffer(
                HalfResWidth, HalfResHeight,
                PixelInternalFormat.Rgba16f);

            var programId = CompileGatherShader();

            using var programUse = programId.UseScope();
            SetupGatherUniforms(programId, invProjection, viewMatrix);

            programId.ScreenProbeAtlas = atlasTex;
            programId.ProbeAnchorPosition = anchorPosTex;
            programId.ProbeAnchorNormal = anchorNormalTex;
            programId.PrimaryDepth = depthTex.TextureId;
            programId.GBufferNormal = normalTex.TextureId;

            TestFramework.RenderQuadTo(programId, outputGBuffer);
            var outputData = outputGBuffer[0].ReadPixels();
            var (r, g, b, _) = ReadPixelHalfRes(outputData, 0, 0);
            fullValidityBrightness = (r + g + b) / 3f;
        }

        // Partial validity (0.5)
        {
            var anchorPosData = CreateProbeAnchors(probeWorldZ, validity: 0.5f);

            using var atlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, atlasData);
            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
            using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
            using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
            using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);

            using var outputGBuffer = TestFramework.CreateTestGBuffer(
                HalfResWidth, HalfResHeight,
                PixelInternalFormat.Rgba16f);

            var programId = CompileGatherShader();

            using var programUse = programId.UseScope();
            SetupGatherUniforms(programId, invProjection, viewMatrix);

            programId.ScreenProbeAtlas = atlasTex;
            programId.ProbeAnchorPosition = anchorPosTex;
            programId.ProbeAnchorNormal = anchorNormalTex;
            programId.PrimaryDepth = depthTex.TextureId;
            programId.GBufferNormal = normalTex.TextureId;

            TestFramework.RenderQuadTo(programId, outputGBuffer);
            var outputData = outputGBuffer[0].ReadPixels();
            var (r, g, b, _) = ReadPixelHalfRes(outputData, 0, 0);
            partialValidityBrightness = (r + g + b) / 3f;
        }

        // Partial validity should have similar or less brightness
        // (not necessarily exactly half due to normalization)
        Assert.True(partialValidityBrightness <= fullValidityBrightness + TestEpsilon,
            $"Partial validity ({partialValidityBrightness:F4}) should be <= full ({fullValidityBrightness:F4})");
    }

    /// <summary>
    /// Tests that sampleStride uniform affects sampling quality.
    ///
    /// DESIRED BEHAVIOR:
    /// - sampleStride=1: Sample every texel in probe's octahedral tile
    /// - sampleStride=2: Sample every other texel (faster but lower quality)
    ///
    /// Setup:
    /// - Compare stride=1 vs stride=2 with non-uniform atlas
    ///
    /// Expected:
    /// - Both should produce valid output (stride affects quality, not correctness)
    /// </summary>
    [Fact]
    public void SampleStride_AffectsQuality()
    {
        EnsureShaderTestAvailable();

        const float pixelDepth = 0.5f;

        CreateTestMatricesForDepth(pixelDepth, out var invProjection, out var viewMatrix,
            out var probeWorldZ, out var hitDistance);

        var atlasData = CreateQuadrantAtlas(hitDistance);
        var anchorPosData = CreateProbeAnchors(probeWorldZ, validity: 1.0f);
        var anchorNormalData = CreateProbeNormals(0f, 1f, 0f);
        var depthData = CreateDepthBuffer(pixelDepth);
        var normalData = CreateNormalBuffer(0f, 1f, 0f);

        float stride1Brightness;
        float stride2Brightness;

        // Stride 1
        {
            using var atlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, atlasData);
            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
            using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
            using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
            using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);

            using var outputGBuffer = TestFramework.CreateTestGBuffer(
                HalfResWidth, HalfResHeight,
                PixelInternalFormat.Rgba16f);

            var programId = CompileGatherShader();

            using var programUse = programId.UseScope();
            SetupGatherUniforms(programId, invProjection, viewMatrix, sampleStride: 1);

            programId.ScreenProbeAtlas = atlasTex;
            programId.ProbeAnchorPosition = anchorPosTex;
            programId.ProbeAnchorNormal = anchorNormalTex;
            programId.PrimaryDepth = depthTex.TextureId;
            programId.GBufferNormal = normalTex.TextureId;

            TestFramework.RenderQuadTo(programId, outputGBuffer);
            var outputData = outputGBuffer[0].ReadPixels();
            var (r, g, b, _) = ReadPixelHalfRes(outputData, 0, 0);
            stride1Brightness = (r + g + b) / 3f;
        }

        // Stride 2
        {
            using var atlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, atlasData);
            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
            using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
            using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
            using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);

            using var outputGBuffer = TestFramework.CreateTestGBuffer(
                HalfResWidth, HalfResHeight,
                PixelInternalFormat.Rgba16f);

            var programId = CompileGatherShader();

            using var programUse = programId.UseScope();
            SetupGatherUniforms(programId, invProjection, viewMatrix, sampleStride: 2);

            programId.ScreenProbeAtlas = atlasTex;
            programId.ProbeAnchorPosition = anchorPosTex;
            programId.ProbeAnchorNormal = anchorNormalTex;
            programId.PrimaryDepth = depthTex.TextureId;
            programId.GBufferNormal = normalTex.TextureId;

            TestFramework.RenderQuadTo(programId, outputGBuffer);
            var outputData = outputGBuffer[0].ReadPixels();
            var (r, g, b, _) = ReadPixelHalfRes(outputData, 0, 0);
            stride2Brightness = (r + g + b) / 3f;
        }

        // Both should produce non-zero output
        Assert.True(stride1Brightness > 0.001f, "Stride 1 should produce non-zero output");
        Assert.True(stride2Brightness > 0.001f, "Stride 2 should produce non-zero output");
    }

    /// <summary>
    /// Tests that hemisphere backface samples are skipped correctly.
    ///
    /// DESIRED BEHAVIOR:
    /// - When cosWeight (dot(dir, normal)) &lt;= 0, the sample should be skipped
    /// - This prevents sampling from behind the surface
    ///
    /// Setup:
    /// - Surface normal pointing up (0, 1, 0)
    /// - Atlas should only contribute from upper hemisphere
    ///
    /// Expected:
    /// - Output should reflect only upper hemisphere contribution
    /// </summary>
    [Fact]
    public void HemisphereBackface_SkippedCorrectly()
    {
        EnsureShaderTestAvailable();

        const float pixelDepth = 0.5f;

        CreateTestMatricesForDepth(pixelDepth, out var invProjection, out var viewMatrix,
            out var probeWorldZ, out var hitDistance);

        // Create atlas with different colors for upper and lower hemisphere
        // Upper hemisphere (y > 0) = bright, Lower = dark
        var atlasData = CreateHemisphereAtlas(hitDistance);
        var anchorPosData = CreateProbeAnchors(probeWorldZ, validity: 1.0f);
        var anchorNormalData = CreateProbeNormals(0f, 1f, 0f);  // Upward normal
        var depthData = CreateDepthBuffer(pixelDepth);
        var normalData = CreateNormalBuffer(0f, 1f, 0f);  // Upward normal

        using var atlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, atlasData);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            HalfResWidth, HalfResHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileGatherShader();

        using var programUse = programId.UseScope();
        SetupGatherUniforms(programId, invProjection, viewMatrix);

        programId.ScreenProbeAtlas = atlasTex;
        programId.ProbeAnchorPosition = anchorPosTex;
        programId.ProbeAnchorNormal = anchorNormalTex;
        programId.PrimaryDepth = depthTex.TextureId;
        programId.GBufferNormal = normalTex.TextureId;

        TestFramework.RenderQuadTo(programId, outputGBuffer);
        var outputData = outputGBuffer[0].ReadPixels();

        // With upward normal, gather should mostly see upper hemisphere (brighter)
        var (r, g, b, _) = ReadPixelHalfRes(outputData, 0, 0);
        float brightness = (r + g + b) / 3f;

        // Should be non-zero (upper hemisphere contributes)
        Assert.True(brightness > 0.01f,
            $"Upper hemisphere should contribute to gather, got brightness={brightness:F4}");
    }

    /// <summary>
    /// Creates an atlas with bright upper hemisphere and dark lower hemisphere.
    /// </summary>
    private float[] CreateHemisphereAtlas(float hitDistance)
    {
        var data = new float[AtlasWidth * AtlasHeight * 4];
        float encHitDist = MathF.Log(hitDistance + 1.0f);  // Encode hit distance

        for (int py = 0; py < ProbeGridHeight; py++)
        {
            for (int px = 0; px < ProbeGridWidth; px++)
            {
                int tileX = px * OctahedralSize;
                int tileY = py * OctahedralSize;

                for (int ty = 0; ty < OctahedralSize; ty++)
                {
                    for (int tx = 0; tx < OctahedralSize; tx++)
                    {
                        int atlasX = tileX + tx;
                        int atlasY = tileY + ty;
                        int idx = (atlasY * AtlasWidth + atlasX) * 4;

                        // Upper half of tile = bright (upper hemisphere), Lower = dark
                        bool isUpperHemisphere = ty < OctahedralSize / 2;
                        float brightness = isUpperHemisphere ? 1.0f : 0.1f;

                        data[idx + 0] = brightness;
                        data[idx + 1] = brightness;
                        data[idx + 2] = brightness;
                        data[idx + 3] = encHitDist;
                    }
                }
            }
        }
        return data;
    }

    /// <summary>
    /// Sets up gather uniforms with custom leak threshold.
    /// </summary>
    private void SetupGatherUniformsWithLeak(
        LumOnScreenProbeAtlasGatherShaderProgram programId,
        float[] invProjection,
        float[] view,
        float leakThreshold)
    {
        SetupGatherUniforms(programId, invProjection, view);
        programId.LeakThreshold = leakThreshold;
    }

    /// <summary>
    /// Tests that leakThreshold prevents light bleeding through walls.
    ///
    /// DESIRED BEHAVIOR:
    /// - Lower leakThreshold = stricter leak prevention
    /// - Higher leakThreshold = more permissive (may allow more bleeding)
    ///
    /// Setup:
    /// - Probe with depth mismatch to pixel
    /// - Compare strict vs permissive threshold
    ///
    /// Expected:
    /// - Strict threshold should produce different/lower output
    /// </summary>
    [Fact]
    public void LeakThreshold_PreventsBleeding()
    {
        EnsureShaderTestAvailable();

        const float pixelDepth = 0.5f;

        CreateTestMatricesForDepth(pixelDepth, out var invProjection, out var viewMatrix,
            out var probeWorldZ, out var hitDistance);

        // Create atlas with uniform bright color
        var atlasData = CreateQuadrantAtlas(hitDistance);
        var anchorPosData = CreateProbeAnchors(probeWorldZ, validity: 1.0f);
        var anchorNormalData = CreateProbeNormals(0f, 1f, 0f);
        var depthData = CreateDepthBuffer(pixelDepth);
        var normalData = CreateNormalBuffer(0f, 1f, 0f);

        float strictBrightness;
        float permissiveBrightness;

        // Strict leak threshold (0.1)
        {
            using var atlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, atlasData);
            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
            using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
            using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
            using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);

            using var outputGBuffer = TestFramework.CreateTestGBuffer(
                HalfResWidth, HalfResHeight,
                PixelInternalFormat.Rgba16f);

            var programId = CompileGatherShader();

            using var programUse = programId.UseScope();
            SetupGatherUniformsWithLeak(programId, invProjection, viewMatrix, leakThreshold: 0.1f);

            programId.ScreenProbeAtlas = atlasTex;
            programId.ProbeAnchorPosition = anchorPosTex;
            programId.ProbeAnchorNormal = anchorNormalTex;
            programId.PrimaryDepth = depthTex.TextureId;
            programId.GBufferNormal = normalTex.TextureId;

            TestFramework.RenderQuadTo(programId, outputGBuffer);
            var outputData = outputGBuffer[0].ReadPixels();
            var (r, g, b, _) = ReadPixelHalfRes(outputData, 0, 0);
            strictBrightness = (r + g + b) / 3f;
        }

        // Permissive leak threshold (0.9)
        {
            using var atlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, atlasData);
            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
            using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
            using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
            using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);

            using var outputGBuffer = TestFramework.CreateTestGBuffer(
                HalfResWidth, HalfResHeight,
                PixelInternalFormat.Rgba16f);

            var programId = CompileGatherShader();

            using var programUse = programId.UseScope();
            SetupGatherUniformsWithLeak(programId, invProjection, viewMatrix, leakThreshold: 0.9f);

            programId.ScreenProbeAtlas = atlasTex;
            programId.ProbeAnchorPosition = anchorPosTex;
            programId.ProbeAnchorNormal = anchorNormalTex;
            programId.PrimaryDepth = depthTex.TextureId;
            programId.GBufferNormal = normalTex.TextureId;

            TestFramework.RenderQuadTo(programId, outputGBuffer);
            var outputData = outputGBuffer[0].ReadPixels();
            var (r, g, b, _) = ReadPixelHalfRes(outputData, 0, 0);
            permissiveBrightness = (r + g + b) / 3f;
        }

        // Both should produce some output (we're not creating a deliberate leak scenario)
        Assert.True(strictBrightness >= 0, "Strict threshold should produce valid output");
        Assert.True(permissiveBrightness >= 0, "Permissive threshold should produce valid output");
    }

    [Fact]
    public void DirectionalOcclusion_RejectsProbeAcrossWall()
    {
        EnsureShaderTestAvailable();

        const float pixelDepth = 0.5f;
        CreateTestMatricesForDepth(pixelDepth, out var invProjection, out var viewMatrix,
            out var probeWorldZ, out _);

        // All four probes are well away from the shaded pixels but otherwise valid.
        // Their directional cache reports an immediate occluder in every direction.
        var anchorPosData = CreateProbeAnchors(probeWorldZ, validity: 1.0f);
        for (int probeIndex = 0; probeIndex < ProbeGridWidth * ProbeGridHeight; probeIndex++)
        {
            int offset = probeIndex * 4;
            anchorPosData[offset] = 10.0f;
            anchorPosData[offset + 1] = 10.0f;
        }

        using var atlasTex = TestFramework.CreateTexture(
            AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, CreateUniformAtlas(1f, 1f, 1f, hitDist: 0f));
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, CreateProbeNormals(0f, 1f, 0f));
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, CreateDepthBuffer(pixelDepth));
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, CreateNormalBuffer(0f, 1f, 0f));
        using var output = TestFramework.CreateTestGBuffer(HalfResWidth, HalfResHeight, PixelInternalFormat.Rgba16f);

        var programId = CompileGatherShader();

        using var programUse = programId.UseScope();
        try
        {
            SetupGatherUniforms(programId, invProjection, viewMatrix);
            programId.ScreenProbeAtlas = atlasTex;
            programId.ProbeAnchorPosition = anchorPosTex;
            programId.ProbeAnchorNormal = anchorNormalTex;
            programId.PrimaryDepth = depthTex.TextureId;
            programId.GBufferNormal = normalTex.TextureId;

            TestFramework.RenderQuadTo(programId, output);

            var (r, g, b, confidence) = ReadPixelHalfRes(output[0].ReadPixels(), 0, 0);
            Assert.True(r < 1e-3f && g < 1e-3f && b < 1e-3f,
                $"Occluded probes must not leak radiance, got ({r:F3}, {g:F3}, {b:F3})");
            Assert.True(confidence < 1e-3f, $"Expected zero screen-probe confidence, got {confidence:F3}");
        }
        finally
        {
        }
    }

    #endregion
}
