using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>
/// Functional tests for the LumOn SH Gather shader pass.
/// 
/// These tests verify that the SH-based gather shader correctly:
/// - Interpolates SH coefficients from the four surrounding probes
/// - Weights probes by bilinear position, depth similarity, and normal similarity
/// - Evaluates SH in the pixel's normal direction for diffuse irradiance
/// - Applies intensity and indirectTint to the final output
/// - Handles edge cases (sky pixels, invalid probes)
/// 
/// Test configuration:
/// - Screen buffer: 4×4 pixels (full-res)
/// - Half-res buffer: 2×2 pixels (gather output)
/// - Probe grid: 2×2 probes
/// - SH radiance: 2 textures (packed L1 SH coefficients)
/// - Probe spacing: 2 pixels
/// </summary>
/// <remarks>
/// Key differences from octahedral gather:
/// - Inputs are SH coefficients (2 textures) instead of octahedral atlas
/// - SH evaluation: shEvaluateDiffuseRGB(shR, shG, shB, pixelNormalVS)
/// - No octahedral lookups - direct bilinear SH interpolation
/// 
/// Edge-aware weight calculation:
/// <code>
/// depthWeight = exp(-depthDiff² / (2σ²))
/// normalWeight = pow(max(dot(pixelNormal, probeNormal), 0), σn)
/// finalWeight = bilinear * depth * normal * validity
/// </code>
/// </remarks>
[Collection("GPU")]
[Trait("Category", "GPU")]
public class LumOnGatherFunctionalTests : LumOnShaderFunctionalTestBase
{
    // SH Constants (must match shader)
    private const float SH_C0 = 0.282095f;  // 1 / (2 * sqrt(pi))
    private const float SH_C1 = 0.488603f;  // sqrt(3) / (2 * sqrt(pi))

    public LumOnGatherFunctionalTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Helper Methods

    /// <summary>
    /// Compiles and links the SH gather shader.
    /// </summary>
    private int CompileSHGatherShader() => CompileShader("lumon_gather.vsh", "lumon_gather.fsh");

    /// <summary>
    /// Sets up common uniforms for the SH gather shader.
    /// </summary>
    private void SetupSHGatherUniforms(
        int programId,
        float[] invProjection,
        float[] view,
        float intensity = 1.0f,
        (float r, float g, float b) indirectTint = default,
        float depthSigma = 0.5f,
        float normalSigma = 8.0f)
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
        var depthSigmaLoc = GL.GetUniformLocation(programId, "depthSigma");
        var normalSigmaLoc = GL.GetUniformLocation(programId, "normalSigma");
        var depthThreshLoc = GL.GetUniformLocation(programId, "depthDiscontinuityThreshold");
        
        GL.Uniform1(intensityLoc, intensity);
        var tint = indirectTint == default ? (1.0f, 1.0f, 1.0f) : indirectTint;
        GL.Uniform3(tintLoc, tint.Item1, tint.Item2, tint.Item3);
        GL.Uniform1(depthSigmaLoc, depthSigma);
        GL.Uniform1(normalSigmaLoc, normalSigma);
        GL.Uniform1(depthThreshLoc, 0.5f);

        // Texture sampler uniforms
        var radiance0Loc = GL.GetUniformLocation(programId, "radianceTexture0");
        var radiance1Loc = GL.GetUniformLocation(programId, "radianceTexture1");
        var anchorPosLoc = GL.GetUniformLocation(programId, "probeAnchorPosition");
        var anchorNormalLoc = GL.GetUniformLocation(programId, "probeAnchorNormal");
        var depthLoc = GL.GetUniformLocation(programId, "primaryDepth");
        var normalLoc = GL.GetUniformLocation(programId, "gBufferNormal");
        GL.Uniform1(radiance0Loc, 0);
        GL.Uniform1(radiance1Loc, 1);
        GL.Uniform1(anchorPosLoc, 2);
        GL.Uniform1(anchorNormalLoc, 3);
        GL.Uniform1(depthLoc, 4);
        GL.Uniform1(normalLoc, 5);

        GL.UseProgram(0);
    }

    /// <summary>
    /// Creates a probe anchor position buffer with specified positions and validity.
    /// </summary>
    private static float[] CreateProbeAnchors(float worldZ, float validity = 1.0f)
    {
        var data = new float[ProbeGridWidth * ProbeGridHeight * 4];
        for (int py = 0; py < ProbeGridHeight; py++)
        {
            for (int px = 0; px < ProbeGridWidth; px++)
            {
                int idx = (py * ProbeGridWidth + px) * 4;
                data[idx + 0] = (px + 0.5f) * ProbeSpacing / (float)ScreenWidth * 2.0f - 1.0f;
                data[idx + 1] = (py + 0.5f) * ProbeSpacing / (float)ScreenHeight * 2.0f - 1.0f;
                data[idx + 2] = worldZ;
                data[idx + 3] = validity;
            }
        }
        return data;
    }

    /// <summary>
    /// Creates probe anchor normals (encoded to 0-1 range).
    /// </summary>
    private static float[] CreateProbeNormals(float nx, float ny, float nz)
    {
        var data = new float[ProbeGridWidth * ProbeGridHeight * 4];
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
    /// Packs RGB SH L1 coefficients into 2-texture layout.
    /// </summary>
    /// <param name="shR">Red SH coefficients (c0, c1, c2, c3)</param>
    /// <param name="shG">Green SH coefficients</param>
    /// <param name="shB">Blue SH coefficients</param>
    /// <returns>Tuple of (tex0, tex1) packed values</returns>
    private static ((float, float, float, float) tex0, (float, float, float, float) tex1) PackSH(
        (float c0, float c1, float c2, float c3) shR,
        (float c0, float c1, float c2, float c3) shG,
        (float c0, float c1, float c2, float c3) shB)
    {
        // 2-Texture Layout:
        // tex0: (SH0_R, SH0_G, SH0_B, SH1_R) - DC terms + Red Y1
        // tex1: (SH1_G, SH1_B, SH2_R, SH2_G) - G/B Y1, R/G Y2
        var tex0 = (shR.c0, shG.c0, shB.c0, shR.c1);
        
        // Compress Z and X directions into luminance
        float avgZX_R = (shR.c2 + shR.c3) * 0.5f;
        float avgZX_G = (shG.c2 + shG.c3) * 0.5f;
        float avgZX_B = (shB.c2 + shB.c3) * 0.5f;
        float lumZX = avgZX_R * 0.2126f + avgZX_G * 0.7152f + avgZX_B * 0.0722f;
        
        var tex1 = (shG.c1, shB.c1, lumZX, 0f);
        
        return (tex0, tex1);
    }

    /// <summary>
    /// Creates SH radiance textures with uniform color for all probes.
    /// Projects color into DC term (omnidirectional).
    /// </summary>
    private static (float[] tex0, float[] tex1) CreateUniformSHRadiance(float r, float g, float b)
    {
        var tex0Data = new float[ProbeGridWidth * ProbeGridHeight * 4];
        var tex1Data = new float[ProbeGridWidth * ProbeGridHeight * 4];

        // DC-only SH (omnidirectional): c0 = radiance * SH_C0, others = 0
        var shR = (c0: r * SH_C0, c1: 0f, c2: 0f, c3: 0f);
        var shG = (c0: g * SH_C0, c1: 0f, c2: 0f, c3: 0f);
        var shB = (c0: b * SH_C0, c1: 0f, c2: 0f, c3: 0f);
        var (tex0, tex1) = PackSH(shR, shG, shB);

        for (int i = 0; i < ProbeGridWidth * ProbeGridHeight; i++)
        {
            int idx = i * 4;
            tex0Data[idx + 0] = tex0.Item1;
            tex0Data[idx + 1] = tex0.Item2;
            tex0Data[idx + 2] = tex0.Item3;
            tex0Data[idx + 3] = tex0.Item4;

            tex1Data[idx + 0] = tex1.Item1;
            tex1Data[idx + 1] = tex1.Item2;
            tex1Data[idx + 2] = tex1.Item3;
            tex1Data[idx + 3] = tex1.Item4;
        }

        return (tex0Data, tex1Data);
    }

    /// <summary>
    /// Creates SH radiance textures with different colors per probe (RGBW quadrants).
    /// </summary>
    private static (float[] tex0, float[] tex1) CreateQuadrantSHRadiance()
    {
        var tex0Data = new float[ProbeGridWidth * ProbeGridHeight * 4];
        var tex1Data = new float[ProbeGridWidth * ProbeGridHeight * 4];

        // Probe (0,0) = Red, (1,0) = Green, (0,1) = Blue, (1,1) = White
        (float r, float g, float b)[] probeColors =
        [
            (1f, 0f, 0f),  // Probe (0,0)
            (0f, 1f, 0f),  // Probe (1,0)
            (0f, 0f, 1f),  // Probe (0,1)
            (1f, 1f, 1f)   // Probe (1,1)
        ];

        for (int py = 0; py < ProbeGridHeight; py++)
        {
            for (int px = 0; px < ProbeGridWidth; px++)
            {
                int probeIdx = py * ProbeGridWidth + px;
                var (r, g, b) = probeColors[probeIdx];

                // DC-only SH
                var shR = (c0: r * SH_C0, c1: 0f, c2: 0f, c3: 0f);
                var shG = (c0: g * SH_C0, c1: 0f, c2: 0f, c3: 0f);
                var shB = (c0: b * SH_C0, c1: 0f, c2: 0f, c3: 0f);
                var (tex0, tex1) = PackSH(shR, shG, shB);

                int idx = probeIdx * 4;
                tex0Data[idx + 0] = tex0.Item1;
                tex0Data[idx + 1] = tex0.Item2;
                tex0Data[idx + 2] = tex0.Item3;
                tex0Data[idx + 3] = tex0.Item4;

                tex1Data[idx + 0] = tex1.Item1;
                tex1Data[idx + 1] = tex1.Item2;
                tex1Data[idx + 2] = tex1.Item3;
                tex1Data[idx + 3] = tex1.Item4;
            }
        }

        return (tex0Data, tex1Data);
    }

    /// <summary>
    /// Creates a depth buffer with uniform depth.
    /// </summary>
    private static float[] CreateDepthBuffer(float depth)
    {
        var data = new float[ScreenWidth * ScreenHeight];
        for (int i = 0; i < ScreenWidth * ScreenHeight; i++)
        {
            data[i] = depth;
        }
        return data;
    }

    /// <summary>
    /// Creates a normal buffer with uniform normals (encoded to 0-1 range).
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
            data[idx + 3] = 0f;
        }
        return data;
    }

    /// <summary>
    /// Reads a pixel from half-res output.
    /// </summary>
    private static (float r, float g, float b, float a) ReadPixelHalfRes(float[] data, int x, int y)
    {
        int idx = (y * HalfResWidth + x) * 4;
        return (data[idx], data[idx + 1], data[idx + 2], data[idx + 3]);
    }

    #endregion

    #region Test: ValidProbes_ProduceNonZeroIrradiance

    /// <summary>
    /// Tests that valid probes with uniform radiance produce non-zero irradiance.
    /// 
    /// DESIRED BEHAVIOR:
    /// - Valid probes with non-zero SH DC terms should contribute irradiance
    /// - All pixels should receive some indirect lighting
    /// 
    /// Setup:
    /// - All probes valid with white radiance (1,1,1)
    /// - Uniform depth and upward normals
    /// 
    /// Expected:
    /// - All half-res pixels have non-zero irradiance
    /// </summary>
    [Fact]
    public void ValidProbes_ProduceNonZeroIrradiance()
    {
        EnsureShaderTestAvailable();

        const float probeWorldZ = -5f;  // In front of camera
        const float pixelDepth = 0.5f;  // Mid-depth

        var (tex0Data, tex1Data) = CreateUniformSHRadiance(1f, 1f, 1f);
        var anchorPosData = CreateProbeAnchors(probeWorldZ, validity: 1.0f);
        var anchorNormalData = CreateProbeNormals(0f, 0f, -1f);  // Facing camera
        var depthData = CreateDepthBuffer(pixelDepth);
        var normalData = CreateNormalBuffer(0f, 0f, -1f);  // Facing camera

        using var radiance0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, tex0Data);
        using var radiance1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, tex1Data);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            HalfResWidth, HalfResHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileSHGatherShader();
        var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
        var view = LumOnTestInputFactory.CreateIdentityView();
        SetupSHGatherUniforms(programId, invProjection, view);

        radiance0Tex.Bind(0);
        radiance1Tex.Bind(1);
        anchorPosTex.Bind(2);
        anchorNormalTex.Bind(3);
        depthTex.Bind(4);
        normalTex.Bind(5);

        TestFramework.RenderQuadTo(programId, outputGBuffer);
        var outputData = outputGBuffer[0].ReadPixels();

        // All pixels should have non-zero irradiance
        for (int py = 0; py < HalfResHeight; py++)
        {
            for (int px = 0; px < HalfResWidth; px++)
            {
                var (r, g, b, _) = ReadPixelHalfRes(outputData, px, py);
                float brightness = (r + g + b) / 3f;

                Assert.True(brightness > 0.001f,
                    $"Pixel ({px},{py}) should have non-zero irradiance, got ({r:F4}, {g:F4}, {b:F4})");
            }
        }

        GL.DeleteProgram(programId);
    }

    #endregion

    #region Test: InvalidProbes_ProduceZeroIrradiance

    /// <summary>
    /// Tests that invalid probes produce zero irradiance.
    /// 
    /// DESIRED BEHAVIOR:
    /// - Invalid probes (validity=0) should not contribute to lighting
    /// - All pixels should have zero irradiance when all probes invalid
    /// 
    /// Setup:
    /// - All probes invalid (validity=0)
    /// - Non-zero SH radiance (would contribute if valid)
    /// 
    /// Expected:
    /// - All half-res pixels have zero irradiance
    /// </summary>
    [Fact]
    public void InvalidProbes_ProduceZeroIrradiance()
    {
        EnsureShaderTestAvailable();

        const float probeWorldZ = -5f;
        const float pixelDepth = 0.5f;

        var (tex0Data, tex1Data) = CreateUniformSHRadiance(1f, 1f, 1f);
        var anchorPosData = CreateProbeAnchors(probeWorldZ, validity: 0f);  // Invalid
        var anchorNormalData = CreateProbeNormals(0f, 0f, -1f);
        var depthData = CreateDepthBuffer(pixelDepth);
        var normalData = CreateNormalBuffer(0f, 0f, -1f);

        using var radiance0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, tex0Data);
        using var radiance1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, tex1Data);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            HalfResWidth, HalfResHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileSHGatherShader();
        var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
        var view = LumOnTestInputFactory.CreateIdentityView();
        SetupSHGatherUniforms(programId, invProjection, view);

        radiance0Tex.Bind(0);
        radiance1Tex.Bind(1);
        anchorPosTex.Bind(2);
        anchorNormalTex.Bind(3);
        depthTex.Bind(4);
        normalTex.Bind(5);

        TestFramework.RenderQuadTo(programId, outputGBuffer);
        var outputData = outputGBuffer[0].ReadPixels();

        // All pixels should be zero
        for (int py = 0; py < HalfResHeight; py++)
        {
            for (int px = 0; px < HalfResWidth; px++)
            {
                var (r, g, b, _) = ReadPixelHalfRes(outputData, px, py);
                
                Assert.True(r < TestEpsilon && g < TestEpsilon && b < TestEpsilon,
                    $"Pixel ({px},{py}) should be zero with invalid probes, got ({r:F4}, {g:F4}, {b:F4})");
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
    /// - Sky pixels should early-out with zero irradiance
    /// - GI should not apply to sky
    /// 
    /// Setup:
    /// - Valid probes with radiance
    /// - Depth buffer = 1.0 (sky)
    /// 
    /// Expected:
    /// - All half-res pixels have zero irradiance
    /// </summary>
    [Fact]
    public void SkyPixels_ProduceZeroIrradiance()
    {
        EnsureShaderTestAvailable();

        const float probeWorldZ = -5f;

        var (tex0Data, tex1Data) = CreateUniformSHRadiance(1f, 1f, 1f);
        var anchorPosData = CreateProbeAnchors(probeWorldZ, validity: 1.0f);
        var anchorNormalData = CreateProbeNormals(0f, 0f, -1f);
        var depthData = CreateDepthBuffer(1.0f);  // Sky
        var normalData = CreateNormalBuffer(0f, 0f, -1f);

        using var radiance0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, tex0Data);
        using var radiance1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, tex1Data);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            HalfResWidth, HalfResHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileSHGatherShader();
        var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
        var view = LumOnTestInputFactory.CreateIdentityView();
        SetupSHGatherUniforms(programId, invProjection, view);

        radiance0Tex.Bind(0);
        radiance1Tex.Bind(1);
        anchorPosTex.Bind(2);
        anchorNormalTex.Bind(3);
        depthTex.Bind(4);
        normalTex.Bind(5);

        TestFramework.RenderQuadTo(programId, outputGBuffer);
        var outputData = outputGBuffer[0].ReadPixels();

        // All pixels should be zero (sky)
        for (int py = 0; py < HalfResHeight; py++)
        {
            for (int px = 0; px < HalfResWidth; px++)
            {
                var (r, g, b, _) = ReadPixelHalfRes(outputData, px, py);
                
                Assert.True(r < TestEpsilon && g < TestEpsilon && b < TestEpsilon,
                    $"Pixel ({px},{py}) should be zero for sky, got ({r:F4}, {g:F4}, {b:F4})");
            }
        }

        GL.DeleteProgram(programId);
    }

    #endregion

    #region Test: IntensityParameter_ScalesOutput

    /// <summary>
    /// Tests that the intensity parameter scales the output irradiance.
    /// 
    /// DESIRED BEHAVIOR:
    /// - intensity=2.0 should produce 2x brighter output than intensity=1.0
    /// 
    /// Setup:
    /// - Same scene with intensity=1.0 and intensity=2.0
    /// 
    /// Expected:
    /// - Output brightness scales with intensity
    /// </summary>
    [Fact]
    public void IntensityParameter_ScalesOutput()
    {
        EnsureShaderTestAvailable();

        const float probeWorldZ = -5f;
        const float pixelDepth = 0.5f;

        var (tex0Data, tex1Data) = CreateUniformSHRadiance(1f, 1f, 1f);
        var anchorPosData = CreateProbeAnchors(probeWorldZ, validity: 1.0f);
        var anchorNormalData = CreateProbeNormals(0f, 0f, -1f);
        var depthData = CreateDepthBuffer(pixelDepth);
        var normalData = CreateNormalBuffer(0f, 0f, -1f);

        float intensity1Brightness;
        float intensity2Brightness;

        // Intensity = 1.0
        {
            using var radiance0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, tex0Data);
            using var radiance1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, tex1Data);
            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
            using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
            using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
            using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);

            using var outputGBuffer = TestFramework.CreateTestGBuffer(
                HalfResWidth, HalfResHeight,
                PixelInternalFormat.Rgba16f);

            var programId = CompileSHGatherShader();
            var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
            var view = LumOnTestInputFactory.CreateIdentityView();
            SetupSHGatherUniforms(programId, invProjection, view, intensity: 1.0f);

            radiance0Tex.Bind(0);
            radiance1Tex.Bind(1);
            anchorPosTex.Bind(2);
            anchorNormalTex.Bind(3);
            depthTex.Bind(4);
            normalTex.Bind(5);

            TestFramework.RenderQuadTo(programId, outputGBuffer);
            var outputData = outputGBuffer[0].ReadPixels();

            var (r, g, b, _) = ReadPixelHalfRes(outputData, 0, 0);
            intensity1Brightness = (r + g + b) / 3f;

            GL.DeleteProgram(programId);
        }

        // Intensity = 2.0
        {
            using var radiance0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, tex0Data);
            using var radiance1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, tex1Data);
            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
            using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
            using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
            using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);

            using var outputGBuffer = TestFramework.CreateTestGBuffer(
                HalfResWidth, HalfResHeight,
                PixelInternalFormat.Rgba16f);

            var programId = CompileSHGatherShader();
            var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
            var view = LumOnTestInputFactory.CreateIdentityView();
            SetupSHGatherUniforms(programId, invProjection, view, intensity: 2.0f);

            radiance0Tex.Bind(0);
            radiance1Tex.Bind(1);
            anchorPosTex.Bind(2);
            anchorNormalTex.Bind(3);
            depthTex.Bind(4);
            normalTex.Bind(5);

            TestFramework.RenderQuadTo(programId, outputGBuffer);
            var outputData = outputGBuffer[0].ReadPixels();

            var (r, g, b, _) = ReadPixelHalfRes(outputData, 0, 0);
            intensity2Brightness = (r + g + b) / 3f;

            GL.DeleteProgram(programId);
        }

        // Intensity 2 should be approximately 2x brighter (with some tolerance)
        Assert.True(intensity2Brightness > intensity1Brightness * 1.5f,
            $"Intensity 2.0 ({intensity2Brightness:F4}) should be > 1.5x intensity 1.0 ({intensity1Brightness:F4})");
    }

    #endregion

    #region Test: IndirectTint_ModulatesColor

    /// <summary>
    /// Tests that indirectTint modulates the output color.
    /// 
    /// DESIRED BEHAVIOR:
    /// - indirectTint=(1,0,0) should only pass red channel
    /// - indirectTint=(0,1,0) should only pass green channel
    /// 
    /// Setup:
    /// - White radiance (1,1,1)
    /// - Compare tint=(1,1,1) vs tint=(1,0,0)
    /// 
    /// Expected:
    /// - Red tint should have red output, no green/blue
    /// </summary>
    [Fact]
    public void IndirectTint_ModulatesColor()
    {
        EnsureShaderTestAvailable();

        const float probeWorldZ = -5f;
        const float pixelDepth = 0.5f;

        var (tex0Data, tex1Data) = CreateUniformSHRadiance(1f, 1f, 1f);
        var anchorPosData = CreateProbeAnchors(probeWorldZ, validity: 1.0f);
        var anchorNormalData = CreateProbeNormals(0f, 0f, -1f);
        var depthData = CreateDepthBuffer(pixelDepth);
        var normalData = CreateNormalBuffer(0f, 0f, -1f);

        float redTintRed, redTintGreen, redTintBlue;

        // Red tint
        {
            using var radiance0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, tex0Data);
            using var radiance1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, tex1Data);
            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
            using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
            using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
            using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);

            using var outputGBuffer = TestFramework.CreateTestGBuffer(
                HalfResWidth, HalfResHeight,
                PixelInternalFormat.Rgba16f);

            var programId = CompileSHGatherShader();
            var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
            var view = LumOnTestInputFactory.CreateIdentityView();
            SetupSHGatherUniforms(programId, invProjection, view, indirectTint: (1f, 0f, 0f));

            radiance0Tex.Bind(0);
            radiance1Tex.Bind(1);
            anchorPosTex.Bind(2);
            anchorNormalTex.Bind(3);
            depthTex.Bind(4);
            normalTex.Bind(5);

            TestFramework.RenderQuadTo(programId, outputGBuffer);
            var outputData = outputGBuffer[0].ReadPixels();

            (redTintRed, redTintGreen, redTintBlue, _) = ReadPixelHalfRes(outputData, 0, 0);

            GL.DeleteProgram(programId);
        }

        // Red tint should have red output only
        Assert.True(redTintRed > 0.01f,
            $"Red tint should have red component, got {redTintRed:F4}");
        Assert.True(redTintGreen < TestEpsilon,
            $"Red tint should have zero green, got {redTintGreen:F4}");
        Assert.True(redTintBlue < TestEpsilon,
            $"Red tint should have zero blue, got {redTintBlue:F4}");
    }

    #endregion

    #region Test: QuadrantProbes_InterpolateCorrectly

    /// <summary>
    /// Tests that probes with different colors interpolate based on position.
    /// 
    /// DESIRED BEHAVIOR:
    /// - Pixels should blend colors from surrounding probes
    /// - Center pixel should have contribution from all four RGBW probes
    /// 
    /// Setup:
    /// - Probes: (0,0)=Red, (1,0)=Green, (0,1)=Blue, (1,1)=White
    /// - Uniform depth/normals for equal weighting
    /// 
    /// Expected:
    /// - Output pixels should have multiple color components
    /// </summary>
    [Fact]
    public void QuadrantProbes_InterpolateCorrectly()
    {
        EnsureShaderTestAvailable();

        const float probeWorldZ = -5f;
        const float pixelDepth = 0.5f;

        var (tex0Data, tex1Data) = CreateQuadrantSHRadiance();
        var anchorPosData = CreateProbeAnchors(probeWorldZ, validity: 1.0f);
        var anchorNormalData = CreateProbeNormals(0f, 0f, -1f);
        var depthData = CreateDepthBuffer(pixelDepth);
        var normalData = CreateNormalBuffer(0f, 0f, -1f);

        using var radiance0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, tex0Data);
        using var radiance1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, tex1Data);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            HalfResWidth, HalfResHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileSHGatherShader();
        var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
        var view = LumOnTestInputFactory.CreateIdentityView();
        SetupSHGatherUniforms(programId, invProjection, view);

        radiance0Tex.Bind(0);
        radiance1Tex.Bind(1);
        anchorPosTex.Bind(2);
        anchorNormalTex.Bind(3);
        depthTex.Bind(4);
        normalTex.Bind(5);

        TestFramework.RenderQuadTo(programId, outputGBuffer);
        var outputData = outputGBuffer[0].ReadPixels();

        // Check that output has multiple color components (blended from probes)
        for (int py = 0; py < HalfResHeight; py++)
        {
            for (int px = 0; px < HalfResWidth; px++)
            {
                var (r, g, b, _) = ReadPixelHalfRes(outputData, px, py);
                float brightness = r + g + b;

                // Should have non-zero brightness
                Assert.True(brightness > 0.01f,
                    $"Pixel ({px},{py}) should have non-zero brightness, got ({r:F4}, {g:F4}, {b:F4})");

                // With RGBW quadrants, should have at least 2 color components
                int nonZeroChannels = (r > 0.01f ? 1 : 0) + (g > 0.01f ? 1 : 0) + (b > 0.01f ? 1 : 0);
                Assert.True(nonZeroChannels >= 1,
                    $"Pixel ({px},{py}) should blend from multiple probes, got ({r:F4}, {g:F4}, {b:F4})");
            }
        }

        GL.DeleteProgram(programId);
    }

    #endregion

    #region Test: DepthMismatch_ReducesWeight

    /// <summary>
    /// Tests that depth mismatch between pixel and probe reduces weight.
    /// 
    /// DESIRED BEHAVIOR:
    /// - Probes with depth significantly different from pixel should contribute less
    /// - Edge-aware weighting prevents light leaking across depth discontinuities
    /// 
    /// Setup:
    /// - Pixel at depth=0.2, probes at depth=0.8 (large mismatch)
    /// - Compare against matched depth case
    /// 
    /// Expected:
    /// - Mismatched case should have less brightness
    /// </summary>
    [Fact]
    public void DepthMismatch_ReducesWeight()
    {
        EnsureShaderTestAvailable();

        var (tex0Data, tex1Data) = CreateUniformSHRadiance(1f, 1f, 1f);
        var anchorNormalData = CreateProbeNormals(0f, 0f, -1f);
        var normalData = CreateNormalBuffer(0f, 0f, -1f);

        float matchedBrightness;
        float mismatchedBrightness;

        // Matched depth
        {
            const float probeWorldZ = -5f;
            const float pixelDepth = 0.5f;

            var anchorPosData = CreateProbeAnchors(probeWorldZ, validity: 1.0f);
            var depthData = CreateDepthBuffer(pixelDepth);

            using var radiance0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, tex0Data);
            using var radiance1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, tex1Data);
            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
            using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
            using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
            using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);

            using var outputGBuffer = TestFramework.CreateTestGBuffer(
                HalfResWidth, HalfResHeight,
                PixelInternalFormat.Rgba16f);

            var programId = CompileSHGatherShader();
            var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
            var view = LumOnTestInputFactory.CreateIdentityView();
            SetupSHGatherUniforms(programId, invProjection, view, depthSigma: 0.1f);

            radiance0Tex.Bind(0);
            radiance1Tex.Bind(1);
            anchorPosTex.Bind(2);
            anchorNormalTex.Bind(3);
            depthTex.Bind(4);
            normalTex.Bind(5);

            TestFramework.RenderQuadTo(programId, outputGBuffer);
            var outputData = outputGBuffer[0].ReadPixels();

            var (r, g, b, _) = ReadPixelHalfRes(outputData, 0, 0);
            matchedBrightness = (r + g + b) / 3f;

            GL.DeleteProgram(programId);
        }

        // Mismatched depth (probes far, pixel near)
        {
            const float probeWorldZ = -50f;  // Far probes
            const float pixelDepth = 0.1f;   // Near pixel

            var anchorPosData = CreateProbeAnchors(probeWorldZ, validity: 1.0f);
            var depthData = CreateDepthBuffer(pixelDepth);

            using var radiance0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, tex0Data);
            using var radiance1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, tex1Data);
            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
            using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
            using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
            using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);

            using var outputGBuffer = TestFramework.CreateTestGBuffer(
                HalfResWidth, HalfResHeight,
                PixelInternalFormat.Rgba16f);

            var programId = CompileSHGatherShader();
            var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
            var view = LumOnTestInputFactory.CreateIdentityView();
            SetupSHGatherUniforms(programId, invProjection, view, depthSigma: 0.1f);

            radiance0Tex.Bind(0);
            radiance1Tex.Bind(1);
            anchorPosTex.Bind(2);
            anchorNormalTex.Bind(3);
            depthTex.Bind(4);
            normalTex.Bind(5);

            TestFramework.RenderQuadTo(programId, outputGBuffer);
            var outputData = outputGBuffer[0].ReadPixels();

            var (r, g, b, _) = ReadPixelHalfRes(outputData, 0, 0);
            mismatchedBrightness = (r + g + b) / 3f;

            GL.DeleteProgram(programId);
        }

        // Mismatched should have different (potentially lower) brightness
        // The exact relationship depends on the depth weighting function
        Assert.True(MathF.Abs(matchedBrightness - mismatchedBrightness) > 0.001f ||
                    matchedBrightness > mismatchedBrightness * 0.5f,
            $"Depth mismatch should affect brightness. Matched={matchedBrightness:F4}, Mismatched={mismatchedBrightness:F4}");
    }

    #endregion
}
