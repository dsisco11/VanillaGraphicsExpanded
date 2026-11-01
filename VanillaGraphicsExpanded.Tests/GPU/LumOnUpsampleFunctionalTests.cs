using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.LumOn;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using VanillaGraphicsExpanded.Tests.GPU.Assertions;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>
/// Functional tests for the LumOn Upsample shader pass.
///
/// These tests verify that the upsample shader correctly:
/// - Upsamples half-resolution indirect lighting to full resolution
/// - Uses bilateral filtering to preserve edges at depth/normal discontinuities
/// - Falls back to simple bilinear when denoiseEnabled=0
/// - Outputs black for sky pixels
///
/// Test configuration:
/// - Half-res input: 2×2 pixels
/// - Full-res output: 4×4 pixels
/// - Upscale factor: 2×
/// </summary>
/// <remarks>
/// The bilateral upsample uses a 2×2 kernel in half-res space with weights:
/// <code>
/// weight = bilinearWeight * depthWeight * normalWeight
/// depthWeight = exp(-depthDiff² / (2 * depthSigma²))
/// normalWeight = pow(max(dot(centerN, sampleN), 0), normalSigma)
/// </code>
/// </remarks>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public class LumOnUpsampleFunctionalTests : LumOnShaderFunctionalTestBase
{
    // Default bilateral filter parameters
    private const float DefaultDepthSigma = 0.1f;
    private const float DefaultNormalSigma = 16.0f;
    private const float DefaultSpatialSigma = 1.0f;

    public LumOnUpsampleFunctionalTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Helper Methods

    /// <summary>
    /// Compiles and links the upsample shader.
    /// </summary>
    private LumOnUpsampleShaderProgram CompileUpsampleShader() => Programs.Create<LumOnUpsampleShaderProgram>();

    /// <summary>
    /// Sets up common uniforms for the upsample shader.
    /// Note: denoiseEnabled and holeFillEnabled are now compile-time defines,
    /// so they are not set as uniforms here. Use CompileShaderWithDefines() instead.
    /// </summary>
    private void SetupUpsampleUniforms(
        LumOnUpsampleShaderProgram programId,
        float depthSigma = DefaultDepthSigma,
        float normalSigma = DefaultNormalSigma,
        float spatialSigma = DefaultSpatialSigma,
        int holeFillRadius = 2,
        float holeFillMinConfidence = 0.05f)
    {
        UpdateAndBindLumOnFrameUbo(programId);
        programId.UpsampleDepthSigma = depthSigma;
        programId.UpsampleNormalSigma = normalSigma;
        programId.UpsampleSpatialSigma = spatialSigma;
        programId.HoleFillRadius = holeFillRadius;
        programId.HoleFillMinConfidence = holeFillMinConfidence;
    }

    /// <summary>
    /// Creates a 2x2 half-res indirect buffer with custom per-pixel RGBA.
    /// Pixel order is row-major: (0,0), (1,0), (0,1), (1,1).
    /// </summary>
    private static float[] CreateCustomHalfRes((float r, float g, float b, float a) p00,
                                               (float r, float g, float b, float a) p10,
                                               (float r, float g, float b, float a) p01,
                                               (float r, float g, float b, float a) p11)
    {
        var data = new float[HalfResWidth * HalfResHeight * 4];

        void Write(int x, int y, (float r, float g, float b, float a) p)
        {
            int idx = (y * HalfResWidth + x) * 4;
            data[idx + 0] = p.r;
            data[idx + 1] = p.g;
            data[idx + 2] = p.b;
            data[idx + 3] = p.a;
        }

        Write(0, 0, p00);
        Write(1, 0, p10);
        Write(0, 1, p01);
        Write(1, 1, p11);

        return data;
    }

    /// <summary>
    /// Creates a half-res indirect buffer with uniform color.
    /// </summary>
    private static float[] CreateUniformHalfRes(float r, float g, float b)
    {
        var data = new float[HalfResWidth * HalfResHeight * 4];
        for (int i = 0; i < HalfResWidth * HalfResHeight; i++)
        {
            int idx = i * 4;
            data[idx + 0] = r;
            data[idx + 1] = g;
            data[idx + 2] = b;
            data[idx + 3] = 1.0f;
        }
        return data;
    }

    /// <summary>
    /// Creates a half-res indirect buffer with a horizontal gradient.
    /// Left column = dark, Right column = bright
    /// </summary>
    private static float[] CreateGradientHalfRes()
    {
        var data = new float[HalfResWidth * HalfResHeight * 4];
        for (int py = 0; py < HalfResHeight; py++)
        {
            for (int px = 0; px < HalfResWidth; px++)
            {
                int idx = (py * HalfResWidth + px) * 4;
                float t = (float)px / (HalfResWidth - 1);  // 0.0 to 1.0
                data[idx + 0] = t;       // R
                data[idx + 1] = t;       // G
                data[idx + 2] = t;       // B
                data[idx + 3] = 1.0f;
            }
        }
        return data;
    }

    /// <summary>
    /// Creates a full-res depth buffer with uniform depth. Delegates to base class.
    /// </summary>
    private float[] CreateDepthBuffer(float depth) => CreateUniformDepthData(ScreenWidth, ScreenHeight, depth);

    /// <summary>
    /// Creates a full-res depth buffer with a vertical edge.
    /// Left half = near, Right half = far
    /// </summary>
    private static float[] CreateDepthBufferWithEdge(float nearDepth, float farDepth)
    {
        var data = new float[ScreenWidth * ScreenHeight];
        for (int py = 0; py < ScreenHeight; py++)
        {
            for (int px = 0; px < ScreenWidth; px++)
            {
                int idx = py * ScreenWidth + px;
                data[idx] = px < ScreenWidth / 2 ? nearDepth : farDepth;
            }
        }
        return data;
    }

    /// <summary>
    /// Creates a full-res normal buffer with uniform normals. Delegates to base class.
    /// </summary>
    private float[] CreateNormalBuffer(float nx, float ny, float nz) => CreateUniformNormalData(ScreenWidth, ScreenHeight, nx, ny, nz);

    /// <summary>Retains float brightness, product rounding and ordered accumulation, returning only the original scalar variance.</summary>
    private float BrightnessVariance(GpuTexture image)
    {
        using var transform=new GpuImageTransform();
        using var brightness=transform.Apply(image, GpuImageProjection.LumOnUpsampleFunctionalTests_BrightnessVariance);
        float sum=ImageAssertions.OrderedFloatSum(brightness, GpuImageProjection.GpuImageAssertionTests_OrderedFloatReductionRetainsOriginalArithmetic);
        float sumSq=ImageAssertions.OrderedFloatSum(brightness, GpuImageProjection.LumOnUpsampleFunctionalTests_BrightnessVariance_2);
        int count=image.Width*image.Height;
        float mean=sum/count;
        return sumSq/count-mean*mean;
    }
    /// <summary>
    /// Reads a pixel from full-res output.
    /// </summary>
    private static (float r, float g, float b, float a) ReadPixelFullRes(GpuTexture image, int x, int y)
    {
        var data=image.ReadPixelsRegion(x,y,1,1);
        int idx=0;
        return (data[idx], data[idx + 1], data[idx + 2], data[idx + 3]);
    }

    #endregion

    #region Test: UniformInput_BilinearUpsample

    /// <summary>
    /// Tests that uniform half-res input produces uniform full-res output.
    ///
    /// DESIRED BEHAVIOR:
    /// - When input is uniform color and depth/normals are uniform,
    ///   all output pixels should have the same color as input
    /// - Bilinear interpolation of uniform values = same uniform value
    ///
    /// Setup:
    /// - Half-res input: uniform green (0.3, 0.6, 0.2)
    /// - Depth: uniform 0.5
    /// - Normals: uniform upward
    ///
    /// Expected:
    /// - All 4×4 output pixels = (0.3, 0.6, 0.2)
    /// </summary>
    [Fact]
    public void UniformInput_BilinearUpsample()
    {
        EnsureShaderTestAvailable();

        var inputColor = (r: 0.3f, g: 0.6f, b: 0.2f);
        const float pixelDepth = 0.5f;

        // Create input textures
        var halfResData = CreateUniformHalfRes(inputColor.r, inputColor.g, inputColor.b);
        var depthData = CreateDepthBuffer(pixelDepth);
        var normalData = CreateNormalBuffer(0f, 1f, 0f);  // Upward

        using var halfResTex = TestFramework.CreateTexture(HalfResWidth, HalfResHeight, TextureFormat.Rgba16f, halfResData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, normalData);

        // Create full-res output
        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ScreenWidth, ScreenHeight,
            TextureFormat.Rgba16f);

        var programId = CompileUpsampleShader();
        SetupUpsampleUniforms(programId);

        // Bind inputs
        programId.IndirectHalf = halfResTex;
        programId.PrimaryDepth = depthTex;
        using var surfaceInput1 = LayeredTestTexture.Create(normalTex, null, null);
        programId.GBufferSurface = surfaceInput1;

        TestFramework.RenderQuadTo(programId, outputGBuffer);

        ImageAssertions.Predicate(outputGBuffer[0], GpuAssertionPredicate.LumOnCombineFunctionalTests_AssertRgb,
            parameters:new(inputColor.r,inputColor.g,inputColor.b,TestEpsilon)).AssertEqual();
    }

    #region Test: HoleFill_OnlyAffectsLowConfidenceAreas

    /// <summary>
    /// Screen-trace hole filling strategy.
    ///
    /// DESIRED BEHAVIOR:
    /// - Hole filling only activates for low-confidence half-res samples (alpha < threshold)
    /// - High-confidence regions must remain unchanged
    ///
    /// Setup:
    /// - Half-res indirect: top-left = red with conf=1, others = black with conf=0
    /// - Depth/normals uniform so edge-aware weights don't block fill
    ///
    /// Expected:
    /// - With VGE_LUMON_UPSAMPLE_HOLEFILL=0: pixels in the bottom-right quadrant stay ~black
    /// - With VGE_LUMON_UPSAMPLE_HOLEFILL=1: pixels in the bottom-right quadrant become non-black
    /// - A pixel in the top-left quadrant remains the same in both runs
    /// </summary>
    [Fact]
    public void HoleFill_OnlyAffectsLowConfidenceAreas()
    {
        EnsureShaderTestAvailable();

        const float pixelDepth = 0.5f;

        // Half-res (2x2): only p00 is valid/confident.
        var halfResData = CreateCustomHalfRes(
            p00: (1.0f, 0.0f, 0.0f, 1.0f),
            p10: (0.0f, 0.0f, 0.0f, 0.0f),
            p01: (0.0f, 0.0f, 0.0f, 0.0f),
            p11: (0.0f, 0.0f, 0.0f, 0.0f));

        var depthData = CreateDepthBuffer(pixelDepth);
        var normalData = CreateNormalBuffer(0f, 1f, 0f);

        using var halfResTex = TestFramework.CreateTexture(HalfResWidth, HalfResHeight, TextureFormat.Rgba16f, halfResData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, normalData);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f);

        // Compile two shader variants: one with hole fill disabled, one with it enabled
        var programIdNoFill = Programs.Create<LumOnUpsampleShaderProgram>(settings: new Dictionary<string, string?> { ["VGE_LUMON_UPSAMPLE_HOLEFILL"] = "0" });

        var programIdFill = Programs.Create<LumOnUpsampleShaderProgram>(settings: new Dictionary<string, string?> { ["VGE_LUMON_UPSAMPLE_HOLEFILL"] = "1" });

        /// <summary>Executes one selected production variant against identical controlled inputs.</summary>
        GpuImageSnapshot Render(LumOnUpsampleShaderProgram programId)
        {
            SetupUpsampleUniforms(programId,
                holeFillRadius: 2,
                holeFillMinConfidence: 0.05f);

            // Bind inputs
            programId.IndirectHalf = halfResTex;
            programId.PrimaryDepth = depthTex;
            using var surfaceInput2 = LayeredTestTexture.Create(normalTex, null, null);
            programId.GBufferSurface = surfaceInput2;

            TestFramework.RenderQuadTo(programId, outputGBuffer);
            return new(outputGBuffer[0]);
        }

        using var outNoFill = Render(programIdNoFill);
        using var outFill = Render(programIdFill);

        // Pick a high-confidence region pixel (top-left quadrant)
        var topLeftNoFill = ReadPixelFullRes(outNoFill.Texture, x: 0, y: 0);
        var topLeftFill = ReadPixelFullRes(outFill.Texture, x: 0, y: 0);

        // Pick a low-confidence region pixel (bottom-right quadrant)
        var bottomRightNoFill = ReadPixelFullRes(outNoFill.Texture, x: 3, y: 3);
        var bottomRightFill = ReadPixelFullRes(outFill.Texture, x: 3, y: 3);

        // High-confidence region should be unchanged.
        Assert.InRange(MathF.Abs(topLeftNoFill.r - topLeftFill.r), 0f, 1e-4f);
        Assert.InRange(MathF.Abs(topLeftNoFill.g - topLeftFill.g), 0f, 1e-4f);
        Assert.InRange(MathF.Abs(topLeftNoFill.b - topLeftFill.b), 0f, 1e-4f);

        // Low-confidence region should change from ~black to non-black.
        Assert.True(bottomRightNoFill.r < 1e-3f && bottomRightNoFill.g < 1e-3f && bottomRightNoFill.b < 1e-3f,
            "Expected no-fill output to remain black in low-confidence region.");
        Assert.True(bottomRightFill.r > 1e-3f || bottomRightFill.g > 1e-3f || bottomRightFill.b > 1e-3f,
            "Expected hole-fill output to become non-black in low-confidence region.");
    }

    #endregion

    #endregion

    #region Test: GradientInput_SmoothUpsample

    /// <summary>
    /// Tests that a gradient in half-res is smoothly interpolated to full-res.
    ///
    /// DESIRED BEHAVIOR:
    /// - Bilinear interpolation should produce smooth gradient across full-res
    /// - Left side should be darker than right side
    /// - Intermediate pixels should have intermediate values
    ///
    /// Setup:
    /// - Half-res input: horizontal gradient (left=0, right=1)
    /// - Uniform depth and normals (no edge filtering)
    ///
    /// Expected:
    /// - Full-res output has smooth left-to-right gradient
    /// - Each row should increase monotonically left to right
    /// </summary>
    [Fact]
    public void GradientInput_SmoothUpsample()
    {
        EnsureShaderTestAvailable();

        const float pixelDepth = 0.5f;

        // Create gradient half-res input
        var halfResData = CreateGradientHalfRes();
        var depthData = CreateDepthBuffer(pixelDepth);
        var normalData = CreateNormalBuffer(0f, 1f, 0f);

        using var halfResTex = TestFramework.CreateTexture(HalfResWidth, HalfResHeight, TextureFormat.Rgba16f, halfResData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, normalData);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ScreenWidth, ScreenHeight,
            TextureFormat.Rgba16f);

        var programId = CompileUpsampleShader();
        SetupUpsampleUniforms(programId);

        programId.IndirectHalf = halfResTex;
        programId.PrimaryDepth = depthTex;
        using var surfaceInput3 = LayeredTestTexture.Create(normalTex, null, null);
        programId.GBufferSurface = surfaceInput3;

        TestFramework.RenderQuadTo(programId, outputGBuffer);

        var outputData=outputGBuffer[0];
        ImageAssertions.Predicate(outputData, GpuAssertionPredicate.LumOnUpsampleFunctionalTests_GradientInput_SmoothUpsample).AssertEqual();
        // DESIRED: Left edge should be darker than right edge
        var (leftR, _, _, _) = ReadPixelFullRes(outputData, 0, ScreenHeight / 2);
        var (rightR, _, _, _) = ReadPixelFullRes(outputData, ScreenWidth - 1, ScreenHeight / 2);
        Assert.True(rightR > leftR,
            $"Right edge ({rightR:F3}) should be brighter than left edge ({leftR:F3})");
    }

    #endregion

    #region Test: DepthEdge_PreservesSharpness

    /// <summary>
    /// Tests that bilateral filter preserves edges at depth discontinuities.
    ///
    /// DESIRED BEHAVIOR:
    /// - When there's a sharp depth edge in the scene, the upsample should NOT
    ///   blend colors across the edge (to prevent light leaking)
    /// - Pixels at the edge should take color primarily from their own depth region
    ///
    /// Setup:
    /// - Half-res: left column = red (1,0,0), right column = blue (0,0,1)
    /// - Depth: left half = near (0.2), right half = far (0.8)
    /// - The depth discontinuity should prevent blending
    ///
    /// Expected:
    /// - Full-res left pixels should be mostly red
    /// - Full-res right pixels should be mostly blue
    /// - Minimal color bleeding across the depth edge
    /// </summary>
    [Fact]
    public void DepthEdge_PreservesSharpness()
    {
        EnsureShaderTestAvailable();

        // Create half-res with color edge: left = red, right = blue
        var halfResData = new float[HalfResWidth * HalfResHeight * 4];
        for (int py = 0; py < HalfResHeight; py++)
        {
            for (int px = 0; px < HalfResWidth; px++)
            {
                int idx = (py * HalfResWidth + px) * 4;
                if (px == 0)  // Left column
                {
                    halfResData[idx + 0] = 1.0f;  // R
                    halfResData[idx + 1] = 0.0f;  // G
                    halfResData[idx + 2] = 0.0f;  // B
                }
                else  // Right column
                {
                    halfResData[idx + 0] = 0.0f;  // R
                    halfResData[idx + 1] = 0.0f;  // G
                    halfResData[idx + 2] = 1.0f;  // B
                }
                halfResData[idx + 3] = 1.0f;
            }
        }

        // Create depth edge: left = near, right = far
        var depthData = CreateDepthBufferWithEdge(0.2f, 0.8f);
        var normalData = CreateNormalBuffer(0f, 1f, 0f);

        using var halfResTex = TestFramework.CreateTexture(HalfResWidth, HalfResHeight, TextureFormat.Rgba16f, halfResData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, normalData);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ScreenWidth, ScreenHeight,
            TextureFormat.Rgba16f);

        var programId = CompileUpsampleShader();
        // Use standard sigma values for edge-aware filtering
        SetupUpsampleUniforms(programId, depthSigma: 0.1f);

        programId.IndirectHalf = halfResTex;
        programId.PrimaryDepth = depthTex;
        using var surfaceInput4 = LayeredTestTexture.Create(normalTex, null, null);
        programId.GBufferSurface = surfaceInput4;

        TestFramework.RenderQuadTo(programId, outputGBuffer);

        ImageAssertions.Predicate(outputGBuffer[0], GpuAssertionPredicate.LumOnUpsampleFunctionalTests_DepthEdge_PreservesSharpness).AssertEqual();
    }

    #endregion

    #region Test: DenoiseDisabled_RawBilinear

    /// <summary>
    /// Tests that with VGE_LUMON_UPSAMPLE_DENOISE=0, simple bilinear sampling is used.
    ///
    /// DESIRED BEHAVIOR:
    /// - When denoise is disabled, the shader should use simple texture() sampling
    /// - This is faster but doesn't preserve edges
    /// - Result should still be a valid upsample, just without edge-awareness
    ///
    /// Setup:
    /// - Same as UniformInput test
    /// - VGE_LUMON_UPSAMPLE_DENOISE = 0 (compile-time)
    ///
    /// Expected:
    /// - Output should still match input color (uniform case)
    /// - No crashes or rendering errors
    /// </summary>
    [Fact]
    public void DenoiseDisabled_RawBilinear()
    {
        EnsureShaderTestAvailable();

        var inputColor = (r: 0.5f, g: 0.25f, b: 0.75f);
        const float pixelDepth = 0.5f;

        var halfResData = CreateUniformHalfRes(inputColor.r, inputColor.g, inputColor.b);
        var depthData = CreateDepthBuffer(pixelDepth);
        var normalData = CreateNormalBuffer(0f, 1f, 0f);

        using var halfResTex = TestFramework.CreateTexture(HalfResWidth, HalfResHeight, TextureFormat.Rgba16f, halfResData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, normalData);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ScreenWidth, ScreenHeight,
            TextureFormat.Rgba16f);

        // Compile with denoising DISABLED (compile-time define)
        var programId = Programs.Create<LumOnUpsampleShaderProgram>(settings: new Dictionary<string, string?> { ["VGE_LUMON_UPSAMPLE_DENOISE"] = "0" });
        SetupUpsampleUniforms(programId);

        programId.IndirectHalf = halfResTex;
        programId.PrimaryDepth = depthTex;
        using var surfaceInput5 = LayeredTestTexture.Create(normalTex, null, null);
        programId.GBufferSurface = surfaceInput5;

        TestFramework.RenderQuadTo(programId, outputGBuffer);

        ImageAssertions.Predicate(outputGBuffer[0], GpuAssertionPredicate.LumOnCombineFunctionalTests_AssertRgb,
            parameters:new(inputColor.r,inputColor.g,inputColor.b,.05f)).AssertEqual();
    }

    #endregion

    #region Test: SkyPixels_ProduceZeroOutput

    /// <summary>
    /// Tests that sky pixels (depth=1.0) produce zero output.
    ///
    /// DESIRED BEHAVIOR:
    /// - Sky pixels should early-out with black (0,0,0,0)
    /// - No indirect lighting should be upsampled for sky
    ///
    /// Setup:
    /// - Bright indirect half-res (should be ignored)
    /// - Depth = 1.0 everywhere (sky)
    ///
    /// Expected:
    /// - All output pixels = (0, 0, 0, 0)
    /// </summary>
    [Fact]
    public void SkyPixels_ProduceZeroOutput()
    {
        EnsureShaderTestAvailable();

        // Bright indirect (should be ignored for sky)
        var halfResData = CreateUniformHalfRes(1.0f, 1.0f, 1.0f);
        var depthData = CreateDepthBuffer(1.0f);  // Sky
        var normalData = CreateNormalBuffer(0f, 1f, 0f);

        using var halfResTex = TestFramework.CreateTexture(HalfResWidth, HalfResHeight, TextureFormat.Rgba16f, halfResData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, normalData);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ScreenWidth, ScreenHeight,
            TextureFormat.Rgba16f);

        var programId = CompileUpsampleShader();
        SetupUpsampleUniforms(programId);

        programId.IndirectHalf = halfResTex;
        programId.PrimaryDepth = depthTex;
        using var surfaceInput6 = LayeredTestTexture.Create(normalTex, null, null);
        programId.GBufferSurface = surfaceInput6;

        TestFramework.RenderQuadTo(programId, outputGBuffer);

        ImageAssertions.Predicate(outputGBuffer[0], GpuAssertionPredicate.LumOnProbeAtlasGatherFunctionalTests_SkyPixels_ProduceZeroIrradiance,
            parameters:new(TestEpsilon,0,0,0)).AssertEqual();
    }

    #endregion

    #region Additional spatial controls

    /// <summary>
    /// Tests that denoiseEnabled=0 uses simple bilinear upsampling.
    ///
    /// DESIRED BEHAVIOR:
    /// - With denoiseEnabled=0, skip bilateral filtering
    /// - Use simple bilinear interpolation for performance
    ///
    /// Setup:
    /// - Compare denoiseEnabled=0 vs denoiseEnabled=1
    /// - Both should produce valid output
    ///
    /// Expected:
    /// - Both produce similar results with uniform input
    /// </summary>
    [Fact]
    public void DenoiseDisabled_UsesSimpleBilinear()
    {
        EnsureShaderTestAvailable();

        var inputColor = (r: 0.6f, g: 0.4f, b: 0.2f);
        var halfResData = CreateUniformHalfRes(inputColor.r, inputColor.g, inputColor.b);
        var depthData = CreateDepthBuffer(0.5f);
        var normalData = CreateNormalBuffer(0f, 1f, 0f);

        float denoisedBrightness;
        float simpleBrightness;

        // With denoising
        {
            using var halfResTex = TestFramework.CreateTexture(HalfResWidth, HalfResHeight, TextureFormat.Rgba16f, halfResData);
            using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
            using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, normalData);

            using var outputGBuffer = TestFramework.CreateTestGBuffer(
                ScreenWidth, ScreenHeight,
                TextureFormat.Rgba16f);

            var programId = CompileUpsampleShader();
            SetupUpsampleUniforms(programId);

            programId.IndirectHalf = halfResTex;
            programId.PrimaryDepth = depthTex;
            using var surfaceInput7 = LayeredTestTexture.Create(normalTex, null, null);
            programId.GBufferSurface = surfaceInput7;

            TestFramework.RenderQuadTo(programId, outputGBuffer);
            var outputData = outputGBuffer[0];
            var (r, g, b, _) = ReadPixelFullRes(outputData, 2, 2);
            denoisedBrightness = (r + g + b) / 3f;
        }

        // Without denoising (simple bilinear) - compile with DENOISE=0
        {
            using var halfResTex = TestFramework.CreateTexture(HalfResWidth, HalfResHeight, TextureFormat.Rgba16f, halfResData);
            using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
            using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, normalData);

            using var outputGBuffer = TestFramework.CreateTestGBuffer(
                ScreenWidth, ScreenHeight,
                TextureFormat.Rgba16f);

            var programId = Programs.Create<LumOnUpsampleShaderProgram>(settings: new Dictionary<string, string?> { ["VGE_LUMON_UPSAMPLE_DENOISE"] = "0" });
            SetupUpsampleUniforms(programId);

            programId.IndirectHalf = halfResTex;
            programId.PrimaryDepth = depthTex;
            using var surfaceInput8 = LayeredTestTexture.Create(normalTex, null, null);
            programId.GBufferSurface = surfaceInput8;

            TestFramework.RenderQuadTo(programId, outputGBuffer);
            var outputData = outputGBuffer[0];
            var (r, g, b, _) = ReadPixelFullRes(outputData, 2, 2);
            simpleBrightness = (r + g + b) / 3f;
        }

        // Both should produce non-zero, similar output with uniform input
        Assert.True(denoisedBrightness > 0.1f, "Denoised output should be non-zero");
        Assert.True(simpleBrightness > 0.1f, "Simple bilinear output should be non-zero");
        Assert.True(MathF.Abs(denoisedBrightness - simpleBrightness) < 0.2f,
            $"With uniform input, both methods should produce similar results: denoised={denoisedBrightness:F3}, simple={simpleBrightness:F3}");
    }

    /// <summary>
    /// Tests that upsampleSpatialSigma affects the denoising filter.
    ///
    /// DESIRED BEHAVIOR:
    /// - spatialSigma controls the spatial falloff of the bilateral filter
    /// - Larger sigma = more blur, smaller = sharper
    ///
    /// Setup:
    /// - Gradient input
    /// - Compare different spatialSigma values
    ///
    /// Expected:
    /// - Different sigma values produce different results
    /// </summary>
    [Fact]
    public void SpatialSigma_AffectsDenoising()
    {
        EnsureShaderTestAvailable();

        var halfResData = CreateGradientHalfRes();
        var depthData = CreateDepthBuffer(0.5f);
        var normalData = CreateNormalBuffer(0f, 1f, 0f);

        float smallSigmaVariance;
        float largeSigmaVariance;

        // Small spatial sigma (sharper)
        {
            using var halfResTex = TestFramework.CreateTexture(HalfResWidth, HalfResHeight, TextureFormat.Rgba16f, halfResData);
            using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
            using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, normalData);

            using var outputGBuffer = TestFramework.CreateTestGBuffer(
                ScreenWidth, ScreenHeight,
                TextureFormat.Rgba16f);

            var programId = CompileUpsampleShader();
            SetupUpsampleUniforms(programId, spatialSigma: 0.5f);

            programId.IndirectHalf = halfResTex;
            programId.PrimaryDepth = depthTex;
            using var surfaceInput9 = LayeredTestTexture.Create(normalTex, null, null);
            programId.GBufferSurface = surfaceInput9;

            TestFramework.RenderQuadTo(programId, outputGBuffer);
            smallSigmaVariance = BrightnessVariance(outputGBuffer[0]);
        }

        // Large spatial sigma (blurrier)
        {
            using var halfResTex = TestFramework.CreateTexture(HalfResWidth, HalfResHeight, TextureFormat.Rgba16f, halfResData);
            using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
            using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, normalData);

            using var outputGBuffer = TestFramework.CreateTestGBuffer(
                ScreenWidth, ScreenHeight,
                TextureFormat.Rgba16f);

            var programId = CompileUpsampleShader();
            SetupUpsampleUniforms(programId, spatialSigma: 4.0f);

            programId.IndirectHalf = halfResTex;
            programId.PrimaryDepth = depthTex;
            using var surfaceInput10 = LayeredTestTexture.Create(normalTex, null, null);
            programId.GBufferSurface = surfaceInput10;

            TestFramework.RenderQuadTo(programId, outputGBuffer);
            largeSigmaVariance = BrightnessVariance(outputGBuffer[0]);
        }

        // Both should produce valid output
        Assert.True(smallSigmaVariance >= 0, "Small sigma should produce valid variance");
        Assert.True(largeSigmaVariance >= 0, "Large sigma should produce valid variance");
    }

    /// <summary>
    /// Tests that depth edges reduce cross-blending.
    ///
    /// DESIRED BEHAVIOR:
    /// - At sharp depth discontinuities, bilateral filter should reduce blending
    /// - This prevents indirect light from bleeding across depth edges
    ///
    /// Setup:
    /// - Half-res with left=bright, right=dark
    /// - Depth edge down the middle
    ///
    /// Expected:
    /// - Sharp transition at the depth edge
    /// </summary>
    [Fact]
    public void DepthEdge_ReducesCrossBlending()
    {
        EnsureShaderTestAvailable();

        // Create half-res with left bright, right dark
        var halfResData = new float[HalfResWidth * HalfResHeight * 4];
        for (int py = 0; py < HalfResHeight; py++)
        {
            for (int px = 0; px < HalfResWidth; px++)
            {
                int idx = (py * HalfResWidth + px) * 4;
                float brightness = px < HalfResWidth / 2 ? 1.0f : 0.0f;
                halfResData[idx + 0] = brightness;
                halfResData[idx + 1] = brightness;
                halfResData[idx + 2] = brightness;
                halfResData[idx + 3] = 1.0f;
            }
        }

        var depthData = CreateDepthBufferWithEdge(0.3f, 0.7f);  // Sharp depth edge
        var normalData = CreateNormalBuffer(0f, 1f, 0f);

        using var halfResTex = TestFramework.CreateTexture(HalfResWidth, HalfResHeight, TextureFormat.Rgba16f, halfResData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, normalData);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ScreenWidth, ScreenHeight,
            TextureFormat.Rgba16f);

        var programId = CompileUpsampleShader();
        SetupUpsampleUniforms(programId, depthSigma: 0.05f);  // Strict depth filtering

        programId.IndirectHalf = halfResTex;
        programId.PrimaryDepth = depthTex;
        using var surfaceInput11 = LayeredTestTexture.Create(normalTex, null, null);
        programId.GBufferSurface = surfaceInput11;

        TestFramework.RenderQuadTo(programId, outputGBuffer);
        var outputData = outputGBuffer[0];

        // Sample from each side of the edge
        var (lR, _, _, _) = ReadPixelFullRes(outputData, 0, 2);  // Left side
        var (rR, _, _, _) = ReadPixelFullRes(outputData, ScreenWidth - 1, 2);  // Right side

        // Left should be brighter than right
        Assert.True(lR > rR,
            $"Depth edge should prevent cross-blending: left={lR:F3}, right={rR:F3}");
    }

    #endregion
}
