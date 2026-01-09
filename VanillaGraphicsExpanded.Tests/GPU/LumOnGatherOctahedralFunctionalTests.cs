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
public class LumOnGatherOctahedralFunctionalTests : RenderTestBase, IDisposable
{
    private readonly HeadlessGLFixture _fixture;
    private readonly ShaderTestHelper? _helper;
    private readonly ShaderTestFramework _framework;
    
    // Test configuration constants
    private const int ScreenWidth = LumOnTestInputFactory.ScreenWidth;   // 4
    private const int ScreenHeight = LumOnTestInputFactory.ScreenHeight; // 4
    private const int HalfResWidth = ScreenWidth / 2;   // 2
    private const int HalfResHeight = ScreenHeight / 2; // 2
    private const int ProbeGridWidth = LumOnTestInputFactory.ProbeGridWidth;   // 2
    private const int ProbeGridHeight = LumOnTestInputFactory.ProbeGridHeight; // 2
    private const int ProbeSpacing = 2;
    
    // Octahedral atlas constants
    private const int OctahedralSize = 8;
    private const int AtlasWidth = ProbeGridWidth * OctahedralSize;   // 16
    private const int AtlasHeight = ProbeGridHeight * OctahedralSize; // 16
    
    private const float ZNear = LumOnTestInputFactory.DefaultZNear;  // 0.1
    private const float ZFar = LumOnTestInputFactory.DefaultZFar;    // 100
    private const float TestEpsilon = 1e-2f;

    public LumOnGatherOctahedralFunctionalTests(HeadlessGLFixture fixture) : base(fixture)
    {
        _fixture = fixture;
        _framework = new ShaderTestFramework();

        if (_fixture.IsContextValid)
        {
            var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
            var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaderincludes");

            if (Directory.Exists(shaderPath) && Directory.Exists(includePath))
            {
                _helper = new ShaderTestHelper(shaderPath, includePath);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _helper?.Dispose();
            _framework.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Helper Methods

    /// <summary>
    /// Compiles and links the octahedral gather shader.
    /// </summary>
    private int CompileGatherShader()
    {
        var result = _helper!.CompileAndLink("lumon_gather_octahedral.vsh", "lumon_gather_octahedral.fsh");
        Assert.True(result.IsSuccess, $"Shader compilation failed: {result.ErrorMessage}");
        return result.ProgramId;
    }

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
    /// Creates a depth buffer with uniform depth (full-res).
    /// </summary>
    private static float[] CreateDepthBuffer(float depth)
    {
        var data = new float[ScreenWidth * ScreenHeight];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = depth;
        }
        return data;
    }

    /// <summary>
    /// Creates a normal buffer with uniform normals (full-res, encoded).
    /// </summary>
    private static float[] CreateNormalBuffer(float nx, float ny, float nz)
    {
        var data = new float[ScreenWidth * ScreenHeight * 4];
        float encX = nx * 0.5f + 0.5f;
        float encY = ny * 0.5f + 0.5f;
        float encZ = nz * 0.5f + 0.5f;
        
        for (int i = 0; i < ScreenWidth * ScreenHeight; i++)
        {
            int idx = i * 4;
            data[idx + 0] = encX;
            data[idx + 1] = encY;
            data[idx + 2] = encZ;
            data[idx + 3] = 1.0f;
        }
        return data;
    }

    /// <summary>
    /// Reads a pixel from half-res output.
    /// </summary>
    private static (float r, float g, float b, float a) ReadPixel(float[] data, int x, int y)
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
        EnsureContextValid();
        Assert.SkipWhen(_helper == null, "ShaderTestHelper not available");

        const float probeDepthZ = -5.0f;  // World-space Z (negative = into screen)
        const float pixelDepth = 0.5f;    // Normalized depth buffer value

        // Create input textures
        // Probes with RGBW colors
        var atlasData = CreateQuadrantAtlas();
        
        // All probes at same depth and with upward normals
        var anchorPosData = CreateProbeAnchors(probeDepthZ, validity: 1.0f);
        var anchorNormalData = CreateProbeNormals(0f, 1f, 0f);  // Upward
        
        // Pixel depth and normal matching probes
        var depthData = CreateDepthBuffer(pixelDepth);
        var normalData = CreateNormalBuffer(0f, 1f, 0f);  // Upward, matching probes

        using var atlasTex = _framework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, atlasData);
        using var anchorPosTex = _framework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = _framework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
        using var depthTex = _framework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var normalTex = _framework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);

        // Create half-res output
        using var outputGBuffer = _framework.CreateTestGBuffer(
            HalfResWidth, HalfResHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileGatherShader();
        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupGatherUniforms(programId, identity, identity, intensity: 1.0f);

        // Bind inputs
        atlasTex.Bind(0);
        anchorPosTex.Bind(1);
        anchorNormalTex.Bind(2);
        depthTex.Bind(3);
        normalTex.Bind(4);

        _framework.RenderQuadTo(programId, outputGBuffer);

        var outputData = outputGBuffer[0].ReadPixels();

        // DESIRED: All pixels should have some contribution from probes
        // The exact blending depends on bilinear weights and hemisphere integration
        // With RGBW probes and uniform depth/normal, output should be grayish
        for (int py = 0; py < HalfResHeight; py++)
        {
            for (int px = 0; px < HalfResWidth; px++)
            {
                var (r, g, b, _) = ReadPixel(outputData, px, py);
                
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
        EnsureContextValid();
        Assert.SkipWhen(_helper == null, "ShaderTestHelper not available");

        const float pixelDepth = 0.5f;

        // Create atlas with only probe (0,0) having color (red), others black
        var atlasData = new float[AtlasWidth * AtlasHeight * 4];
        float encodedDist = MathF.Log(11.0f);  // log(10 + 1)
        
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

        var anchorPosData = CreateProbeAnchors(-5.0f, validity: 1.0f);
        var anchorNormalData = CreateProbeNormals(0f, 1f, 0f);
        var depthData = CreateDepthBuffer(pixelDepth);
        var normalData = CreateNormalBuffer(0f, 1f, 0f);

        using var atlasTex = _framework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, atlasData);
        using var anchorPosTex = _framework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = _framework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
        using var depthTex = _framework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var normalTex = _framework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);

        using var outputGBuffer = _framework.CreateTestGBuffer(
            HalfResWidth, HalfResHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileGatherShader();
        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupGatherUniforms(programId, identity, identity);

        atlasTex.Bind(0);
        anchorPosTex.Bind(1);
        anchorNormalTex.Bind(2);
        depthTex.Bind(3);
        normalTex.Bind(4);

        _framework.RenderQuadTo(programId, outputGBuffer);

        var outputData = outputGBuffer[0].ReadPixels();

        // DESIRED: Pixel (0,0) should be predominantly red since it's nearest to probe (0,0)
        var (r00, g00, b00, _) = ReadPixel(outputData, 0, 0);
        
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
        EnsureContextValid();
        Assert.SkipWhen(_helper == null, "ShaderTestHelper not available");

        // Pixel at near depth
        const float pixelDepth = 0.3f;  // Near
        
        // Create probes at different depths
        var anchorPosData = new float[ProbeGridWidth * ProbeGridHeight * 4];
        
        // Probe (0,0): near depth, valid
        anchorPosData[0] = -0.5f; anchorPosData[1] = -0.5f; anchorPosData[2] = -2.0f; anchorPosData[3] = 1.0f;
        // Probe (1,0): invalid
        anchorPosData[4] = 0.5f; anchorPosData[5] = -0.5f; anchorPosData[6] = -2.0f; anchorPosData[7] = 0.0f;
        // Probe (0,1): invalid
        anchorPosData[8] = -0.5f; anchorPosData[9] = 0.5f; anchorPosData[10] = -2.0f; anchorPosData[11] = 0.0f;
        // Probe (1,1): far depth, valid
        anchorPosData[12] = 0.5f; anchorPosData[13] = 0.5f; anchorPosData[14] = -50.0f; anchorPosData[15] = 1.0f;

        // Create atlas: probe (0,0) = red, probe (1,1) = blue
        var atlasData = new float[AtlasWidth * AtlasHeight * 4];
        float encodedDist = MathF.Log(11.0f);
        
        // Probe (0,0) = red
        for (int ty = 0; ty < OctahedralSize; ty++)
        {
            for (int tx = 0; tx < OctahedralSize; tx++)
            {
                int idx = (ty * AtlasWidth + tx) * 4;
                atlasData[idx + 0] = 1.0f; atlasData[idx + 1] = 0.0f; atlasData[idx + 2] = 0.0f;
                atlasData[idx + 3] = encodedDist;
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
                atlasData[idx + 3] = encodedDist;
            }
        }

        var anchorNormalData = CreateProbeNormals(0f, 1f, 0f);
        var depthData = CreateDepthBuffer(pixelDepth);
        var normalData = CreateNormalBuffer(0f, 1f, 0f);

        using var atlasTex = _framework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, atlasData);
        using var anchorPosTex = _framework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = _framework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
        using var depthTex = _framework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var normalTex = _framework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);

        using var outputGBuffer = _framework.CreateTestGBuffer(
            HalfResWidth, HalfResHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileGatherShader();
        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupGatherUniforms(programId, identity, identity);

        atlasTex.Bind(0);
        anchorPosTex.Bind(1);
        anchorNormalTex.Bind(2);
        depthTex.Bind(3);
        normalTex.Bind(4);

        _framework.RenderQuadTo(programId, outputGBuffer);

        var outputData = outputGBuffer[0].ReadPixels();

        // DESIRED: Pixel (0,0) should favor red (near probe) over blue (far probe)
        var (r, g, b, _) = ReadPixel(outputData, 0, 0);
        
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
        EnsureContextValid();
        Assert.SkipWhen(_helper == null, "ShaderTestHelper not available");

        const float pixelDepth = 0.5f;

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
        float encodedDist = MathF.Log(11.0f);
        
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

        var anchorPosData = CreateProbeAnchors(-5.0f, validity: 1.0f);
        var depthData = CreateDepthBuffer(pixelDepth);
        var normalData = CreateNormalBuffer(0f, 1f, 0f);  // Pixel normal = upward

        using var atlasTex = _framework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, atlasData);
        using var anchorPosTex = _framework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = _framework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
        using var depthTex = _framework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var normalTex = _framework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);

        using var outputGBuffer = _framework.CreateTestGBuffer(
            HalfResWidth, HalfResHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileGatherShader();
        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupGatherUniforms(programId, identity, identity);

        atlasTex.Bind(0);
        anchorPosTex.Bind(1);
        anchorNormalTex.Bind(2);
        depthTex.Bind(3);
        normalTex.Bind(4);

        _framework.RenderQuadTo(programId, outputGBuffer);

        var outputData = outputGBuffer[0].ReadPixels();

        // DESIRED: Pixel (1,1) should have very little blue contribution
        // because probe (1,1) has opposite normal (dot product ≈ -1, weight ≈ 0)
        var (r11, g11, b11, _) = ReadPixel(outputData, 1, 1);
        
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
        EnsureContextValid();
        Assert.SkipWhen(_helper == null, "ShaderTestHelper not available");

        const float pixelDepth = 0.5f;
        var tint = (r: 2.0f, g: 1.0f, b: 0.5f);

        // Uniform white atlas
        var atlasData = CreateUniformAtlas(1.0f, 1.0f, 1.0f);
        var anchorPosData = CreateProbeAnchors(-5.0f, validity: 1.0f);
        var anchorNormalData = CreateProbeNormals(0f, 1f, 0f);
        var depthData = CreateDepthBuffer(pixelDepth);
        var normalData = CreateNormalBuffer(0f, 1f, 0f);

        using var atlasTex = _framework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, atlasData);
        using var anchorPosTex = _framework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = _framework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
        using var depthTex = _framework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var normalTex = _framework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);

        // First render without tint to get baseline
        using var baselineOutput = _framework.CreateTestGBuffer(
            HalfResWidth, HalfResHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileGatherShader();
        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupGatherUniforms(programId, identity, identity, intensity: 1.0f, indirectTint: (1f, 1f, 1f));

        atlasTex.Bind(0);
        anchorPosTex.Bind(1);
        anchorNormalTex.Bind(2);
        depthTex.Bind(3);
        normalTex.Bind(4);

        _framework.RenderQuadTo(programId, baselineOutput);
        var baselineData = baselineOutput[0].ReadPixels();

        // Now render with tint
        using var tintedOutput = _framework.CreateTestGBuffer(
            HalfResWidth, HalfResHeight,
            PixelInternalFormat.Rgba16f);

        SetupGatherUniforms(programId, identity, identity, intensity: 1.0f, indirectTint: tint);

        _framework.RenderQuadTo(programId, tintedOutput);
        var tintedData = tintedOutput[0].ReadPixels();

        // DESIRED: Tinted output should be baseline * tint per channel
        for (int py = 0; py < HalfResHeight; py++)
        {
            for (int px = 0; px < HalfResWidth; px++)
            {
                var (baseR, baseG, baseB, _) = ReadPixel(baselineData, px, py);
                var (tintR, tintG, tintB, _) = ReadPixel(tintedData, px, py);

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
        EnsureContextValid();
        Assert.SkipWhen(_helper == null, "ShaderTestHelper not available");

        // Sky depth
        var depthData = CreateDepthBuffer(1.0f);
        
        // Bright atlas (should be ignored)
        var atlasData = CreateUniformAtlas(1.0f, 1.0f, 1.0f);
        var anchorPosData = CreateProbeAnchors(-5.0f, validity: 1.0f);
        var anchorNormalData = CreateProbeNormals(0f, 1f, 0f);
        var normalData = CreateNormalBuffer(0f, 1f, 0f);

        using var atlasTex = _framework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, atlasData);
        using var anchorPosTex = _framework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = _framework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
        using var depthTex = _framework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var normalTex = _framework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);

        using var outputGBuffer = _framework.CreateTestGBuffer(
            HalfResWidth, HalfResHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileGatherShader();
        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupGatherUniforms(programId, identity, identity);

        atlasTex.Bind(0);
        anchorPosTex.Bind(1);
        anchorNormalTex.Bind(2);
        depthTex.Bind(3);
        normalTex.Bind(4);

        _framework.RenderQuadTo(programId, outputGBuffer);

        var outputData = outputGBuffer[0].ReadPixels();

        // DESIRED: All sky pixels should output zero irradiance
        for (int py = 0; py < HalfResHeight; py++)
        {
            for (int px = 0; px < HalfResWidth; px++)
            {
                var (r, g, b, _) = ReadPixel(outputData, px, py);
                
                Assert.True(r < TestEpsilon && g < TestEpsilon && b < TestEpsilon,
                    $"Sky pixel ({px},{py}) should have zero irradiance, got ({r:F3}, {g:F3}, {b:F3})");
            }
        }

        GL.DeleteProgram(programId);
    }

    #endregion
}
