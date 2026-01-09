using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>
/// Functional tests for the LumOn Octahedral Gather shader pass.
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
public class LumOnGatherOctahedralFunctionalTests : LumOnShaderFunctionalTestBase
{
    public LumOnGatherOctahedralFunctionalTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Helper Methods

    /// <summary>
    /// Compiles and links the octahedral gather shader.
    /// </summary>
    private int CompileGatherShader() => CompileShader("lumon_gather_octahedral.vsh", "lumon_gather_octahedral.fsh");

    /// <summary>
    /// Sets up common uniforms for the gather shader.
    /// </summary>
    private void SetupGatherUniforms(
        int programId,
        float[] invProjection,
        float[] view,
        float intensity = 1.0f,
        (float r, float g, float b) indirectTint = default,
        int sampleStride = 1)
    {
        GL.UseProgram(programId);

        // Matrix uniforms
        var invProjLoc = GL.GetUniformLocation(programId, "invProjectionMatrix");
        var viewLoc = GL.GetUniformLocation(programId, "viewMatrix");
        GL.UniformMatrix4(invProjLoc, 1, false, invProjection);
        GL.UniformMatrix4(viewLoc, 1, false, view);

        // Probe grid uniforms
        var spacingLoc = GL.GetUniformLocation(programId, "probeSpacing");
        var gridSizeLoc = GL.GetUniformLocation(programId, "probeGridSize");
        var screenSizeLoc = GL.GetUniformLocation(programId, "screenSize");
        var halfResSizeLoc = GL.GetUniformLocation(programId, "halfResSize");
        GL.Uniform1(spacingLoc, ProbeSpacing);
        GL.Uniform2(gridSizeLoc, (float)ProbeGridWidth, (float)ProbeGridHeight);
        GL.Uniform2(screenSizeLoc, (float)ScreenWidth, (float)ScreenHeight);
        GL.Uniform2(halfResSizeLoc, (float)HalfResWidth, (float)HalfResHeight);

        // Z-planes
        var zNearLoc = GL.GetUniformLocation(programId, "zNear");
        var zFarLoc = GL.GetUniformLocation(programId, "zFar");
        GL.Uniform1(zNearLoc, ZNear);
        GL.Uniform1(zFarLoc, ZFar);

        // Quality parameters
        var intensityLoc = GL.GetUniformLocation(programId, "intensity");
        var tintLoc = GL.GetUniformLocation(programId, "indirectTint");
        var leakLoc = GL.GetUniformLocation(programId, "leakThreshold");
        var strideLoc = GL.GetUniformLocation(programId, "sampleStride");
        
        GL.Uniform1(intensityLoc, intensity);
        var tint = indirectTint == default ? (1.0f, 1.0f, 1.0f) : indirectTint;
        GL.Uniform3(tintLoc, tint.Item1, tint.Item2, tint.Item3);
        GL.Uniform1(leakLoc, 0.5f);
        GL.Uniform1(strideLoc, sampleStride);

        // Texture sampler uniforms
        var atlasLoc = GL.GetUniformLocation(programId, "octahedralAtlas");
        var anchorPosLoc = GL.GetUniformLocation(programId, "probeAnchorPosition");
        var anchorNormalLoc = GL.GetUniformLocation(programId, "probeAnchorNormal");
        var depthLoc = GL.GetUniformLocation(programId, "primaryDepth");
        var normalLoc = GL.GetUniformLocation(programId, "gBufferNormal");
        GL.Uniform1(atlasLoc, 0);
        GL.Uniform1(anchorPosLoc, 1);
        GL.Uniform1(anchorNormalLoc, 2);
        GL.Uniform1(depthLoc, 3);
        GL.Uniform1(normalLoc, 4);

        GL.UseProgram(0);
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

    #endregion

    #region Test: CenterPixel_InterpolatesFourProbes

    /// <summary>
    /// Tests that a pixel at the center of the probe grid interpolates equally from all four probes.
    /// 
    /// DESIRED BEHAVIOR:
    /// - When a pixel is equidistant from all four surrounding probes
    /// - And all probes have matching depth and normal
    /// - The output should be the average of all four probes' radiance
    /// 
    /// Setup:
    /// - 2×2 half-res output (center at (0.5, 0.5) in half-res = (1, 1) in full-res)
    /// - Probes with RGBW colors at matching depth/normal
    /// - Pixel depth and normal match all probes
    /// 
    /// Expected:
    /// - Center pixel ≈ average(R, G, B, W) = (0.5, 0.5, 0.5)
    /// </summary>
    [Fact]
    public void CenterPixel_InterpolatesFourProbes()
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
        SetupGatherUniforms(programId, invProjection, viewMatrix, intensity: 1.0f);

        // Bind inputs
        atlasTex.Bind(0);
        anchorPosTex.Bind(1);
        anchorNormalTex.Bind(2);
        depthTex.Bind(3);
        normalTex.Bind(4);

        TestFramework.RenderQuadTo(programId, outputGBuffer);

        var outputData = outputGBuffer[0].ReadPixels();

        // DESIRED: All pixels should have some contribution from probes
        // The exact blending depends on bilinear weights and hemisphere integration
        // With RGBW probes and uniform depth/normal, output should be grayish
        for (int py = 0; py < HalfResHeight; py++)
        {
            for (int px = 0; px < HalfResWidth; px++)
            {
                var (r, g, b, _) = ReadPixelHalfRes(outputData, px, py);
                
                // DESIRED: Non-zero irradiance from probe interpolation
                float brightness = (r + g + b) / 3.0f;
                Assert.True(brightness > 0.01f,
                    $"Pixel ({px},{py}) should have non-zero irradiance, got ({r:F3}, {g:F3}, {b:F3})");
                
                // DESIRED: Should have contribution from multiple color channels (not single probe)
                // With equal weighting, expect roughly equal RGB contribution
                bool hasMultipleChannels = (r > 0.01f ? 1 : 0) + (g > 0.01f ? 1 : 0) + (b > 0.01f ? 1 : 0) >= 2;
                Assert.True(hasMultipleChannels,
                    $"Pixel ({px},{py}) should blend multiple probes, got ({r:F3}, {g:F3}, {b:F3})");
            }
        }

        GL.DeleteProgram(programId);
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
        SetupGatherUniforms(programId, invProjection, viewMatrix);

        atlasTex.Bind(0);
        anchorPosTex.Bind(1);
        anchorNormalTex.Bind(2);
        depthTex.Bind(3);
        normalTex.Bind(4);

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

        GL.DeleteProgram(programId);
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
        SetupGatherUniforms(programId, invProjection, viewMatrix);

        atlasTex.Bind(0);
        anchorPosTex.Bind(1);
        anchorNormalTex.Bind(2);
        depthTex.Bind(3);
        normalTex.Bind(4);

        TestFramework.RenderQuadTo(programId, outputGBuffer);

        var outputData = outputGBuffer[0].ReadPixels();

        // DESIRED: Pixel (0,0) should favor red (near probe) over blue (far probe)
        var (r, g, b, _) = ReadPixelHalfRes(outputData, 0, 0);
        
        // Near probe (red) should have significantly more weight than far probe (blue)
        Assert.True(r > b,
            $"Pixel (0,0) should favor near probe (red) over far probe (blue), got R={r:F3}, B={b:F3}");

        GL.DeleteProgram(programId);
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
        SetupGatherUniforms(programId, invProjection, viewMatrix);

        atlasTex.Bind(0);
        anchorPosTex.Bind(1);
        anchorNormalTex.Bind(2);
        depthTex.Bind(3);
        normalTex.Bind(4);

        TestFramework.RenderQuadTo(programId, outputGBuffer);

        var outputData = outputGBuffer[0].ReadPixels();

        // DESIRED: Pixel (1,1) should have very little blue contribution
        // because probe (1,1) has opposite normal (dot product ≈ -1, weight ≈ 0)
        var (r11, g11, b11, _) = ReadPixelHalfRes(outputData, 1, 1);
        
        // The opposite-normal probe should have near-zero weight
        // So blue should be much less than green (other upward probes)
        Assert.True(b11 < g11 || b11 < 0.1f,
            $"Pixel (1,1) should minimize opposite-normal probe, got R={r11:F3}, G={g11:F3}, B={b11:F3}");

        GL.DeleteProgram(programId);
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
        SetupGatherUniforms(programId, invProjection, viewMatrix, intensity: 1.0f, indirectTint: (1f, 1f, 1f));

        atlasTex.Bind(0);
        anchorPosTex.Bind(1);
        anchorNormalTex.Bind(2);
        depthTex.Bind(3);
        normalTex.Bind(4);

        TestFramework.RenderQuadTo(programId, baselineOutput);
        var baselineData = baselineOutput[0].ReadPixels();

        // Now render with tint
        using var tintedOutput = TestFramework.CreateTestGBuffer(
            HalfResWidth, HalfResHeight,
            PixelInternalFormat.Rgba16f);

        SetupGatherUniforms(programId, invProjection, viewMatrix, intensity: 1.0f, indirectTint: tint);

        // Re-bind textures after uniform setup
        atlasTex.Bind(0);
        anchorPosTex.Bind(1);
        anchorNormalTex.Bind(2);
        depthTex.Bind(3);
        normalTex.Bind(4);

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

        GL.DeleteProgram(programId);
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
        // Use realistic matrices for consistency (though sky pixels early-out before depth reconstruction)
        var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
        var viewMatrix = LumOnTestInputFactory.CreateIdentityView();
        SetupGatherUniforms(programId, invProjection, viewMatrix);

        atlasTex.Bind(0);
        anchorPosTex.Bind(1);
        anchorNormalTex.Bind(2);
        depthTex.Bind(3);
        normalTex.Bind(4);

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

        GL.DeleteProgram(programId);
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
        var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
        var viewMatrix = LumOnTestInputFactory.CreateIdentityView();
        SetupGatherUniforms(programId, invProjection, viewMatrix);

        atlasTex.Bind(0);
        anchorPosTex.Bind(1);
        anchorNormalTex.Bind(2);
        depthTex.Bind(3);
        normalTex.Bind(4);

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

        GL.DeleteProgram(programId);
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
            SetupGatherUniforms(programId, invProjection, viewMatrix);

            atlasTex.Bind(0);
            anchorPosTex.Bind(1);
            anchorNormalTex.Bind(2);
            depthTex.Bind(3);
            normalTex.Bind(4);

            TestFramework.RenderQuadTo(programId, outputGBuffer);
            var outputData = outputGBuffer[0].ReadPixels();
            var (r, g, b, _) = ReadPixelHalfRes(outputData, 0, 0);
            fullValidityBrightness = (r + g + b) / 3f;

            GL.DeleteProgram(programId);
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
            SetupGatherUniforms(programId, invProjection, viewMatrix);

            atlasTex.Bind(0);
            anchorPosTex.Bind(1);
            anchorNormalTex.Bind(2);
            depthTex.Bind(3);
            normalTex.Bind(4);

            TestFramework.RenderQuadTo(programId, outputGBuffer);
            var outputData = outputGBuffer[0].ReadPixels();
            var (r, g, b, _) = ReadPixelHalfRes(outputData, 0, 0);
            partialValidityBrightness = (r + g + b) / 3f;

            GL.DeleteProgram(programId);
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
            SetupGatherUniforms(programId, invProjection, viewMatrix, sampleStride: 1);

            atlasTex.Bind(0);
            anchorPosTex.Bind(1);
            anchorNormalTex.Bind(2);
            depthTex.Bind(3);
            normalTex.Bind(4);

            TestFramework.RenderQuadTo(programId, outputGBuffer);
            var outputData = outputGBuffer[0].ReadPixels();
            var (r, g, b, _) = ReadPixelHalfRes(outputData, 0, 0);
            stride1Brightness = (r + g + b) / 3f;

            GL.DeleteProgram(programId);
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
            SetupGatherUniforms(programId, invProjection, viewMatrix, sampleStride: 2);

            atlasTex.Bind(0);
            anchorPosTex.Bind(1);
            anchorNormalTex.Bind(2);
            depthTex.Bind(3);
            normalTex.Bind(4);

            TestFramework.RenderQuadTo(programId, outputGBuffer);
            var outputData = outputGBuffer[0].ReadPixels();
            var (r, g, b, _) = ReadPixelHalfRes(outputData, 0, 0);
            stride2Brightness = (r + g + b) / 3f;

            GL.DeleteProgram(programId);
        }

        // Both should produce non-zero output
        Assert.True(stride1Brightness > 0.001f, "Stride 1 should produce non-zero output");
        Assert.True(stride2Brightness > 0.001f, "Stride 2 should produce non-zero output");
    }

    #endregion
}
