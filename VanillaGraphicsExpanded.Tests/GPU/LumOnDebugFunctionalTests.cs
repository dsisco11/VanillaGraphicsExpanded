using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>
/// Functional tests for the LumOn Debug Visualization shader pass.
/// 
/// These tests verify that the debug shader correctly renders various
/// visualization modes for debugging the probe grid system.
/// 
/// Debug Modes:
/// 1 = Probe Grid with validity coloring
/// 2 = Probe Depth heatmap
/// 3 = Probe Normals
/// 4 = Scene Depth (linearized)
/// 5 = Scene Normals (G-buffer)
/// 6 = Temporal Weight
/// 7 = Temporal Rejection Mask
/// 8 = SH Coefficients
/// 9 = Interpolation Weights
/// 10 = Radiance Overlay (indirect diffuse)
/// 
/// Test configuration:
/// - Screen buffer: 4×4 pixels
/// - Probe grid: 2×2 probes
/// - Probe spacing: 2 pixels
/// </summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public class LumOnDebugFunctionalTests : LumOnShaderFunctionalTestBase
{
    // Debug mode constants
    private const int MODE_PROBE_GRID = 1;
    private const int MODE_PROBE_DEPTH = 2;
    private const int MODE_PROBE_NORMAL = 3;
    private const int MODE_SCENE_DEPTH = 4;
    private const int MODE_SCENE_NORMAL = 5;
    private const int MODE_TEMPORAL_WEIGHT = 6;
    private const int MODE_TEMPORAL_REJECTION = 7;
    private const int MODE_SH_COEFFICIENTS = 8;
    private const int MODE_INTERPOLATION_WEIGHTS = 9;
    private const int MODE_RADIANCE_OVERLAY = 10;

    public LumOnDebugFunctionalTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Helper Methods

    /// <summary>
    /// Compiles and links the debug visualization shader.
    /// </summary>
    private int CompileDebugShader() => CompileShader("lumon_debug.vsh", "lumon_debug.fsh");

    /// <summary>
    /// Sets up common uniforms for the debug shader.
    /// </summary>
    private void SetupDebugUniforms(
        int programId,
        int debugMode,
        float[] invProjection,
        float[] invView,
        float[] prevViewProj,
        float temporalAlpha = 0.9f,
        float depthRejectThreshold = 0.1f,
        float normalRejectThreshold = 0.9f)
    {
        GL.UseProgram(programId);

        // Debug mode
        var modeLoc = GL.GetUniformLocation(programId, "debugMode");
        GL.Uniform1(modeLoc, debugMode);

        // Size uniforms
        var screenSizeLoc = GL.GetUniformLocation(programId, "screenSize");
        var gridSizeLoc = GL.GetUniformLocation(programId, "probeGridSize");
        var spacingLoc = GL.GetUniformLocation(programId, "probeSpacing");
        GL.Uniform2(screenSizeLoc, (float)ScreenWidth, (float)ScreenHeight);
        GL.Uniform2(gridSizeLoc, (float)ProbeGridWidth, (float)ProbeGridHeight);
        GL.Uniform1(spacingLoc, ProbeSpacing);

        // Z-planes
        var zNearLoc = GL.GetUniformLocation(programId, "zNear");
        var zFarLoc = GL.GetUniformLocation(programId, "zFar");
        GL.Uniform1(zNearLoc, ZNear);
        GL.Uniform1(zFarLoc, ZFar);

        // Matrix uniforms
        var invProjLoc = GL.GetUniformLocation(programId, "invProjectionMatrix");
        var invViewLoc = GL.GetUniformLocation(programId, "invViewMatrix");
        var prevViewProjLoc = GL.GetUniformLocation(programId, "prevViewProjMatrix");
        GL.UniformMatrix4(invProjLoc, 1, false, invProjection);
        GL.UniformMatrix4(invViewLoc, 1, false, invView);
        GL.UniformMatrix4(prevViewProjLoc, 1, false, prevViewProj);

        // Temporal parameters
        var alphaLoc = GL.GetUniformLocation(programId, "temporalAlpha");
        var depthThreshLoc = GL.GetUniformLocation(programId, "depthRejectThreshold");
        var normalThreshLoc = GL.GetUniformLocation(programId, "normalRejectThreshold");
        GL.Uniform1(alphaLoc, temporalAlpha);
        GL.Uniform1(depthThreshLoc, depthRejectThreshold);
        GL.Uniform1(normalThreshLoc, normalRejectThreshold);

        // Texture sampler uniforms
        var depthLoc = GL.GetUniformLocation(programId, "primaryDepth");
        var normalLoc = GL.GetUniformLocation(programId, "gBufferNormal");
        var anchorPosLoc = GL.GetUniformLocation(programId, "probeAnchorPosition");
        var anchorNormalLoc = GL.GetUniformLocation(programId, "probeAnchorNormal");
        var radiance0Loc = GL.GetUniformLocation(programId, "radianceTexture0");
        var radiance1Loc = GL.GetUniformLocation(programId, "radianceTexture1");
        var indirectHalfLoc = GL.GetUniformLocation(programId, "indirectHalf");
        var historyMetaLoc = GL.GetUniformLocation(programId, "historyMeta");
        
        GL.Uniform1(depthLoc, 0);
        GL.Uniform1(normalLoc, 1);
        GL.Uniform1(anchorPosLoc, 2);
        GL.Uniform1(anchorNormalLoc, 3);
        GL.Uniform1(radiance0Loc, 4);
        GL.Uniform1(radiance1Loc, 5);
        GL.Uniform1(indirectHalfLoc, 6);
        GL.Uniform1(historyMetaLoc, 7);

        GL.UseProgram(0);
    }

    /// <summary>
    /// Creates a probe anchor position buffer with mixed validity.
    /// Probe (0,0) = valid, (1,0) = edge, (0,1) = invalid, (1,1) = valid
    /// </summary>
    private static float[] CreateMixedValidityProbeAnchors(float worldZ)
    {
        var data = new float[ProbeGridWidth * ProbeGridHeight * 4];
        float[] validity = [1.0f, 0.5f, 0.0f, 1.0f];  // Valid, Edge, Invalid, Valid

        for (int py = 0; py < ProbeGridHeight; py++)
        {
            for (int px = 0; px < ProbeGridWidth; px++)
            {
                int probeIdx = py * ProbeGridWidth + px;
                int idx = probeIdx * 4;
                data[idx + 0] = (px + 0.5f) * ProbeSpacing / (float)ScreenWidth * 2f - 1f;
                data[idx + 1] = (py + 0.5f) * ProbeSpacing / (float)ScreenHeight * 2f - 1f;
                data[idx + 2] = worldZ;
                data[idx + 3] = validity[probeIdx];
            }
        }
        return data;
    }

    /// <summary>
    /// Creates uniform probe anchors with specified validity.
    /// </summary>
    private static float[] CreateProbeAnchors(float worldZ, float validity = 1.0f)
    {
        var data = new float[ProbeGridWidth * ProbeGridHeight * 4];
        for (int py = 0; py < ProbeGridHeight; py++)
        {
            for (int px = 0; px < ProbeGridWidth; px++)
            {
                int idx = (py * ProbeGridWidth + px) * 4;
                data[idx + 0] = (px + 0.5f) * ProbeSpacing / (float)ScreenWidth * 2f - 1f;
                data[idx + 1] = (py + 0.5f) * ProbeSpacing / (float)ScreenHeight * 2f - 1f;
                data[idx + 2] = worldZ;
                data[idx + 3] = validity;
            }
        }
        return data;
    }

    /// <summary>
    /// Creates probe normals (encoded to 0-1 range).
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
    /// Creates a normal buffer with uniform normals (encoded).
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
    /// Creates SH radiance texture data with uniform DC values.
    /// </summary>
    private static float[] CreateUniformSHRadiance(float r, float g, float b)
    {
        const float SH_C0 = 0.282095f;
        var data = new float[ProbeGridWidth * ProbeGridHeight * 4];

        for (int i = 0; i < ProbeGridWidth * ProbeGridHeight; i++)
        {
            int idx = i * 4;
            data[idx + 0] = r * SH_C0;  // DC R
            data[idx + 1] = g * SH_C0;  // DC G
            data[idx + 2] = b * SH_C0;  // DC B
            data[idx + 3] = 0f;         // Y1 R
        }
        return data;
    }

    /// <summary>
    /// Creates history meta texture.
    /// </summary>
    private static float[] CreateHistoryMeta(float linearDepth, float nx, float ny, float nz, float accumCount)
    {
        var data = new float[ProbeGridWidth * ProbeGridHeight * 4];
        for (int i = 0; i < ProbeGridWidth * ProbeGridHeight; i++)
        {
            int idx = i * 4;
            data[idx + 0] = linearDepth;
            data[idx + 1] = nx * 0.5f + 0.5f;
            data[idx + 2] = ny * 0.5f + 0.5f;
            data[idx + 3] = accumCount;
        }
        return data;
    }

    /// <summary>
    /// Reads a pixel from output.
    /// </summary>
    private static new (float r, float g, float b, float a) ReadPixel(float[] data, int x, int y, int width)
    {
        int idx = (y * width + x) * 4;
        return (data[idx], data[idx + 1], data[idx + 2], data[idx + 3]);
    }

    /// <summary>
    /// Checks if a color is approximately equal to expected values.
    /// </summary>
    private static bool ColorApprox((float r, float g, float b) actual, float er, float eg, float eb, float tolerance = 0.15f)
    {
        return MathF.Abs(actual.r - er) < tolerance &&
               MathF.Abs(actual.g - eg) < tolerance &&
               MathF.Abs(actual.b - eb) < tolerance;
    }

    #endregion

    #region Test: UnknownMode_RendersMagenta

    /// <summary>
    /// Tests that unknown debug modes render magenta (error color).
    /// 
    /// DESIRED BEHAVIOR:
    /// - Invalid debugMode values should output magenta (1,0,1)
    /// - This helps identify misconfiguration
    /// 
    /// Setup:
    /// - debugMode = 99 (invalid)
    /// 
    /// Expected:
    /// - All pixels = magenta (1, 0, 1)
    /// </summary>
    [Fact]
    public void UnknownMode_RendersMagenta()
    {
        EnsureShaderTestAvailable();

        const float probeWorldZ = -5f;

        // Create minimal input textures
        var depthData = CreateDepthBuffer(0.5f);
        var normalData = CreateNormalBuffer(0f, 0f, -1f);
        var anchorPosData = CreateProbeAnchors(probeWorldZ);
        var anchorNormalData = CreateProbeNormals(0f, 0f, -1f);
        var radianceData = CreateUniformSHRadiance(1f, 1f, 1f);
        var historyMetaData = CreateHistoryMeta(5f, 0f, 0f, -1f, 1f);

        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
        using var radiance0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, radianceData);
        using var radiance1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, radianceData);
        using var indirectHalfTex = TestFramework.CreateTexture(HalfResWidth, HalfResHeight, PixelInternalFormat.Rgba16f, new float[HalfResWidth * HalfResHeight * 4]);
        using var historyMetaTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyMetaData);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ScreenWidth, ScreenHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileDebugShader();
        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupDebugUniforms(programId, debugMode: 99, identity, identity, identity);

        depthTex.Bind(0);
        normalTex.Bind(1);
        anchorPosTex.Bind(2);
        anchorNormalTex.Bind(3);
        radiance0Tex.Bind(4);
        radiance1Tex.Bind(5);
        indirectHalfTex.Bind(6);
        historyMetaTex.Bind(7);

        TestFramework.RenderQuadTo(programId, outputGBuffer);
        var outputData = outputGBuffer[0].ReadPixels();

        // All pixels should be magenta
        int magentaCount = 0;
        for (int y = 0; y < ScreenHeight; y++)
        {
            for (int x = 0; x < ScreenWidth; x++)
            {
                var (r, g, b, _) = ReadPixel(outputData, x, y, ScreenWidth);
                if (ColorApprox((r, g, b), 1f, 0f, 1f))
                {
                    magentaCount++;
                }
            }
        }

        Assert.True(magentaCount == ScreenWidth * ScreenHeight,
            $"Expected all {ScreenWidth * ScreenHeight} pixels to be magenta, got {magentaCount}");

        GL.DeleteProgram(programId);
    }

    #endregion

    #region Test: Mode1_ProbeGrid_ShowsValidityColors

    /// <summary>
    /// Tests that Mode 1 (Probe Grid) colors probes by validity.
    /// 
    /// DESIRED BEHAVIOR:
    /// - Green dots for valid probes (validity > 0.9)
    /// - Yellow dots for edge probes (0.4 < validity ≤ 0.9)
    /// - Red dots for invalid probes (validity ≤ 0.4)
    /// 
    /// Setup:
    /// - Mixed validity probes
    /// 
    /// Expected:
    /// - Probe dot colors match validity states
    /// </summary>
    [Fact]
    public void Mode1_ProbeGrid_ShowsValidityColors()
    {
        EnsureShaderTestAvailable();

        const float probeWorldZ = -5f;

        var depthData = CreateDepthBuffer(0.5f);
        var normalData = CreateNormalBuffer(0f, 0f, -1f);
        var anchorPosData = CreateMixedValidityProbeAnchors(probeWorldZ);
        var anchorNormalData = CreateProbeNormals(0f, 0f, -1f);
        var radianceData = CreateUniformSHRadiance(1f, 1f, 1f);
        var historyMetaData = CreateHistoryMeta(5f, 0f, 0f, -1f, 1f);

        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
        using var radiance0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, radianceData);
        using var radiance1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, radianceData);
        using var indirectHalfTex = TestFramework.CreateTexture(HalfResWidth, HalfResHeight, PixelInternalFormat.Rgba16f, new float[HalfResWidth * HalfResHeight * 4]);
        using var historyMetaTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyMetaData);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ScreenWidth, ScreenHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileDebugShader();
        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupDebugUniforms(programId, debugMode: MODE_PROBE_GRID, identity, identity, identity);

        depthTex.Bind(0);
        normalTex.Bind(1);
        anchorPosTex.Bind(2);
        anchorNormalTex.Bind(3);
        radiance0Tex.Bind(4);
        radiance1Tex.Bind(5);
        indirectHalfTex.Bind(6);
        historyMetaTex.Bind(7);

        TestFramework.RenderQuadTo(programId, outputGBuffer);
        var outputData = outputGBuffer[0].ReadPixels();

        // Verify that some pixels have green (valid), yellow (edge), or red (invalid) colors
        bool hasGreen = false, hasYellow = false, hasRed = false;

        for (int y = 0; y < ScreenHeight; y++)
        {
            for (int x = 0; x < ScreenWidth; x++)
            {
                var (r, g, b, _) = ReadPixel(outputData, x, y, ScreenWidth);

                // Green = (0, 1, 0)
                if (g > 0.8f && r < 0.2f && b < 0.2f)
                    hasGreen = true;
                // Yellow = (1, 1, 0)
                if (r > 0.8f && g > 0.8f && b < 0.2f)
                    hasYellow = true;
                // Red = (1, 0, 0)
                if (r > 0.8f && g < 0.2f && b < 0.2f)
                    hasRed = true;
            }
        }

        // With mixed validity, we should see at least valid (green) and invalid (red)
        Assert.True(hasGreen || hasYellow || hasRed,
            "Mode 1 should show colored probe dots based on validity");
    }

    #endregion

    #region Test: Mode4_SceneDepth_ShowsHeatmap

    /// <summary>
    /// Tests that Mode 4 (Scene Depth) renders a depth heatmap.
    /// 
    /// DESIRED BEHAVIOR:
    /// - Non-sky pixels show heatmap colors based on depth
    /// - Sky pixels (depth=1.0) show black
    /// 
    /// Setup:
    /// - Mid-depth scene (depth=0.5)
    /// 
    /// Expected:
    /// - Non-zero colored output (heatmap)
    /// </summary>
    [Fact]
    public void Mode4_SceneDepth_ShowsHeatmap()
    {
        EnsureShaderTestAvailable();

        const float probeWorldZ = -5f;

        var depthData = CreateDepthBuffer(0.5f);  // Mid-depth
        var normalData = CreateNormalBuffer(0f, 0f, -1f);
        var anchorPosData = CreateProbeAnchors(probeWorldZ);
        var anchorNormalData = CreateProbeNormals(0f, 0f, -1f);
        var radianceData = CreateUniformSHRadiance(1f, 1f, 1f);
        var historyMetaData = CreateHistoryMeta(5f, 0f, 0f, -1f, 1f);

        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
        using var radiance0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, radianceData);
        using var radiance1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, radianceData);
        using var indirectHalfTex = TestFramework.CreateTexture(HalfResWidth, HalfResHeight, PixelInternalFormat.Rgba16f, new float[HalfResWidth * HalfResHeight * 4]);
        using var historyMetaTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyMetaData);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ScreenWidth, ScreenHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileDebugShader();
        var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupDebugUniforms(programId, debugMode: MODE_SCENE_DEPTH, invProjection, identity, identity);

        depthTex.Bind(0);
        normalTex.Bind(1);
        anchorPosTex.Bind(2);
        anchorNormalTex.Bind(3);
        radiance0Tex.Bind(4);
        radiance1Tex.Bind(5);
        indirectHalfTex.Bind(6);
        historyMetaTex.Bind(7);

        TestFramework.RenderQuadTo(programId, outputGBuffer);
        var outputData = outputGBuffer[0].ReadPixels();

        // All pixels should have non-black color (heatmap)
        int coloredCount = 0;
        for (int y = 0; y < ScreenHeight; y++)
        {
            for (int x = 0; x < ScreenWidth; x++)
            {
                var (r, g, b, _) = ReadPixel(outputData, x, y, ScreenWidth);
                float brightness = r + g + b;
                if (brightness > 0.1f)
                {
                    coloredCount++;
                }
            }
        }

        Assert.True(coloredCount == ScreenWidth * ScreenHeight,
            $"Mode 4 should show heatmap for all non-sky pixels, got {coloredCount}/{ScreenWidth * ScreenHeight}");

        GL.DeleteProgram(programId);
    }

    #endregion

    #region Test: Mode10_RadianceOverlay_ShowsIndirectDiffuse

    /// <summary>
    /// Tests that Mode 10 (Radiance Overlay) outputs the indirect diffuse buffer.
    /// 
    /// DESIRED BEHAVIOR:
    /// - Non-sky pixels show tone-mapped indirect radiance
    /// 
    /// Setup:
    /// - Depth = 0.5 (non-sky)
    /// - indirectHalf = uniform white (1,1,1)
    /// 
    /// Expected:
    /// - Output ~= Reinhard(1) = 0.5 per channel
    /// </summary>
    [Fact]
    public void Mode10_RadianceOverlay_ShowsIndirectDiffuse()
    {
        EnsureShaderTestAvailable();

        const float probeWorldZ = -5f;

        var depthData = CreateDepthBuffer(0.5f);
        var normalData = CreateNormalBuffer(0f, 0f, -1f);
        var anchorPosData = CreateProbeAnchors(probeWorldZ);
        var anchorNormalData = CreateProbeNormals(0f, 0f, -1f);
        var radianceData = CreateUniformSHRadiance(1f, 1f, 1f);
        var historyMetaData = CreateHistoryMeta(5f, 0f, 0f, -1f, 1f);

        // Uniform indirect diffuse (half-res). RGBA float array.
        var indirectData = new float[HalfResWidth * HalfResHeight * 4];
        for (int i = 0; i < HalfResWidth * HalfResHeight; i++)
        {
            int idx = i * 4;
            indirectData[idx + 0] = 1f;
            indirectData[idx + 1] = 1f;
            indirectData[idx + 2] = 1f;
            indirectData[idx + 3] = 1f;
        }

        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
        using var radiance0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, radianceData);
        using var radiance1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, radianceData);
        using var indirectHalfTex = TestFramework.CreateTexture(HalfResWidth, HalfResHeight, PixelInternalFormat.Rgba16f, indirectData);
        using var historyMetaTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyMetaData);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ScreenWidth, ScreenHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileDebugShader();
        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupDebugUniforms(programId, debugMode: MODE_RADIANCE_OVERLAY, identity, identity, identity);

        depthTex.Bind(0);
        normalTex.Bind(1);
        anchorPosTex.Bind(2);
        anchorNormalTex.Bind(3);
        radiance0Tex.Bind(4);
        radiance1Tex.Bind(5);
        indirectHalfTex.Bind(6);
        historyMetaTex.Bind(7);

        TestFramework.RenderQuadTo(programId, outputGBuffer);
        var outputData = outputGBuffer[0].ReadPixels();

        // Reinhard tone map: 1/(1+1) = 0.5
        for (int y = 0; y < ScreenHeight; y++)
        {
            for (int x = 0; x < ScreenWidth; x++)
            {
                var (r, g, b, _) = ReadPixel(outputData, x, y, ScreenWidth);
                Assert.True(ColorApprox((r, g, b), 0.5f, 0.5f, 0.5f, tolerance: 0.2f),
                    $"Pixel ({x},{y}) expected ~0.5 gray, got ({r:F3},{g:F3},{b:F3})");
            }
        }

        GL.DeleteProgram(programId);
    }

    #endregion

    #region Test: Mode4_SceneDepth_SkyIsBlack

    /// <summary>
    /// Tests that Mode 4 shows black for sky pixels.
    /// 
    /// Setup:
    /// - Depth = 1.0 (sky)
    /// 
    /// Expected:
    /// - All pixels = black
    /// </summary>
    [Fact]
    public void Mode4_SceneDepth_SkyIsBlack()
    {
        EnsureShaderTestAvailable();

        const float probeWorldZ = -5f;

        var depthData = CreateDepthBuffer(1.0f);  // Sky
        var normalData = CreateNormalBuffer(0f, 0f, -1f);
        var anchorPosData = CreateProbeAnchors(probeWorldZ);
        var anchorNormalData = CreateProbeNormals(0f, 0f, -1f);
        var radianceData = CreateUniformSHRadiance(1f, 1f, 1f);
        var historyMetaData = CreateHistoryMeta(5f, 0f, 0f, -1f, 1f);

        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
        using var radiance0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, radianceData);
        using var radiance1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, radianceData);
        using var indirectHalfTex = TestFramework.CreateTexture(HalfResWidth, HalfResHeight, PixelInternalFormat.Rgba16f, new float[HalfResWidth * HalfResHeight * 4]);
        using var historyMetaTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyMetaData);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ScreenWidth, ScreenHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileDebugShader();
        var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupDebugUniforms(programId, debugMode: MODE_SCENE_DEPTH, invProjection, identity, identity);

        depthTex.Bind(0);
        normalTex.Bind(1);
        anchorPosTex.Bind(2);
        anchorNormalTex.Bind(3);
        radiance0Tex.Bind(4);
        radiance1Tex.Bind(5);
        indirectHalfTex.Bind(6);
        historyMetaTex.Bind(7);

        TestFramework.RenderQuadTo(programId, outputGBuffer);
        var outputData = outputGBuffer[0].ReadPixels();

        // All pixels should be black
        int blackCount = 0;
        for (int y = 0; y < ScreenHeight; y++)
        {
            for (int x = 0; x < ScreenWidth; x++)
            {
                var (r, g, b, _) = ReadPixel(outputData, x, y, ScreenWidth);
                if (r < 0.01f && g < 0.01f && b < 0.01f)
                {
                    blackCount++;
                }
            }
        }

        Assert.True(blackCount == ScreenWidth * ScreenHeight,
            $"Mode 4 should show black for sky, got {blackCount}/{ScreenWidth * ScreenHeight}");

        GL.DeleteProgram(programId);
    }

    #endregion

    #region Test: Mode5_SceneNormals_ShowsEncodedNormals

    /// <summary>
    /// Tests that Mode 5 (Scene Normals) visualizes G-buffer normals.
    /// 
    /// DESIRED BEHAVIOR:
    /// - Normals displayed as RGB: (nx*0.5+0.5, ny*0.5+0.5, nz*0.5+0.5)
    /// - Upward normal (0,1,0) should show as (0.5, 1.0, 0.5)
    /// 
    /// Setup:
    /// - Uniform upward normals
    /// 
    /// Expected:
    /// - Output color ≈ (0.5, 1.0, 0.5) greenish
    /// </summary>
    [Fact]
    public void Mode5_SceneNormals_ShowsEncodedNormals()
    {
        EnsureShaderTestAvailable();

        const float probeWorldZ = -5f;

        var depthData = CreateDepthBuffer(0.5f);
        var normalData = CreateNormalBuffer(0f, 1f, 0f);  // Upward
        var anchorPosData = CreateProbeAnchors(probeWorldZ);
        var anchorNormalData = CreateProbeNormals(0f, 1f, 0f);
        var radianceData = CreateUniformSHRadiance(1f, 1f, 1f);
        var historyMetaData = CreateHistoryMeta(5f, 0f, 1f, 0f, 1f);

        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
        using var radiance0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, radianceData);
        using var radiance1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, radianceData);
        using var indirectHalfTex = TestFramework.CreateTexture(HalfResWidth, HalfResHeight, PixelInternalFormat.Rgba16f, new float[HalfResWidth * HalfResHeight * 4]);
        using var historyMetaTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyMetaData);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ScreenWidth, ScreenHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileDebugShader();
        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupDebugUniforms(programId, debugMode: MODE_SCENE_NORMAL, identity, identity, identity);

        depthTex.Bind(0);
        normalTex.Bind(1);
        anchorPosTex.Bind(2);
        anchorNormalTex.Bind(3);
        radiance0Tex.Bind(4);
        radiance1Tex.Bind(5);
        indirectHalfTex.Bind(6);
        historyMetaTex.Bind(7);

        TestFramework.RenderQuadTo(programId, outputGBuffer);
        var outputData = outputGBuffer[0].ReadPixels();

        // Check that output shows upward normal color: (0.5, 1.0, 0.5)
        var (r, g, b, _) = ReadPixel(outputData, ScreenWidth / 2, ScreenHeight / 2, ScreenWidth);

        Assert.True(g > 0.8f,
            $"Mode 5 with upward normal should have high green component, got ({r:F3}, {g:F3}, {b:F3})");

        GL.DeleteProgram(programId);
    }

    #endregion

    #region Test: Mode8_SHCoefficients_ShowsRadiance

    /// <summary>
    /// Tests that Mode 8 (SH Coefficients) visualizes SH radiance data.
    /// 
    /// DESIRED BEHAVIOR:
    /// - Shows DC term as base color
    /// - Tone-mapped for HDR values
    /// 
    /// Setup:
    /// - White radiance (1,1,1) in SH DC terms
    /// 
    /// Expected:
    /// - Non-zero grayscale output (tone-mapped white)
    /// </summary>
    [Fact]
    public void Mode8_SHCoefficients_ShowsRadiance()
    {
        EnsureShaderTestAvailable();

        const float probeWorldZ = -5f;

        var depthData = CreateDepthBuffer(0.5f);
        var normalData = CreateNormalBuffer(0f, 0f, -1f);
        var anchorPosData = CreateProbeAnchors(probeWorldZ);
        var anchorNormalData = CreateProbeNormals(0f, 0f, -1f);
        var radianceData = CreateUniformSHRadiance(1f, 1f, 1f);
        var historyMetaData = CreateHistoryMeta(5f, 0f, 0f, -1f, 1f);

        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
        using var radiance0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, radianceData);
        using var radiance1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, radianceData);
        using var indirectHalfTex = TestFramework.CreateTexture(HalfResWidth, HalfResHeight, PixelInternalFormat.Rgba16f, new float[HalfResWidth * HalfResHeight * 4]);
        using var historyMetaTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyMetaData);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ScreenWidth, ScreenHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileDebugShader();
        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupDebugUniforms(programId, debugMode: MODE_SH_COEFFICIENTS, identity, identity, identity);

        depthTex.Bind(0);
        normalTex.Bind(1);
        anchorPosTex.Bind(2);
        anchorNormalTex.Bind(3);
        radiance0Tex.Bind(4);
        radiance1Tex.Bind(5);
        indirectHalfTex.Bind(6);
        historyMetaTex.Bind(7);

        TestFramework.RenderQuadTo(programId, outputGBuffer);
        var outputData = outputGBuffer[0].ReadPixels();

        // Should have some non-zero output from SH visualization
        int nonZeroCount = 0;
        for (int y = 0; y < ScreenHeight; y++)
        {
            for (int x = 0; x < ScreenWidth; x++)
            {
                var (r, g, b, _) = ReadPixel(outputData, x, y, ScreenWidth);
                if (r > 0.01f || g > 0.01f || b > 0.01f)
                {
                    nonZeroCount++;
                }
            }
        }

        Assert.True(nonZeroCount > 0,
            "Mode 8 should show non-zero SH visualization for valid probes");

        GL.DeleteProgram(programId);
    }

    #endregion

    #region Test: Mode2_ProbeDepth_InvalidProbesBlack

    /// <summary>
    /// Tests that Mode 2 (Probe Depth) shows black for invalid probes.
    /// 
    /// Setup:
    /// - All probes invalid (validity=0)
    /// 
    /// Expected:
    /// - All pixels = black
    /// </summary>
    [Fact]
    public void Mode2_ProbeDepth_InvalidProbesBlack()
    {
        EnsureShaderTestAvailable();

        const float probeWorldZ = -5f;

        var depthData = CreateDepthBuffer(0.5f);
        var normalData = CreateNormalBuffer(0f, 0f, -1f);
        var anchorPosData = CreateProbeAnchors(probeWorldZ, validity: 0f);  // Invalid
        var anchorNormalData = CreateProbeNormals(0f, 0f, -1f);
        var radianceData = CreateUniformSHRadiance(1f, 1f, 1f);
        var historyMetaData = CreateHistoryMeta(5f, 0f, 0f, -1f, 1f);

        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
        using var radiance0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, radianceData);
        using var radiance1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, radianceData);
        using var indirectHalfTex = TestFramework.CreateTexture(HalfResWidth, HalfResHeight, PixelInternalFormat.Rgba16f, new float[HalfResWidth * HalfResHeight * 4]);
        using var historyMetaTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyMetaData);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ScreenWidth, ScreenHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileDebugShader();
        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupDebugUniforms(programId, debugMode: MODE_PROBE_DEPTH, identity, identity, identity);

        depthTex.Bind(0);
        normalTex.Bind(1);
        anchorPosTex.Bind(2);
        anchorNormalTex.Bind(3);
        radiance0Tex.Bind(4);
        radiance1Tex.Bind(5);
        indirectHalfTex.Bind(6);
        historyMetaTex.Bind(7);

        TestFramework.RenderQuadTo(programId, outputGBuffer);
        var outputData = outputGBuffer[0].ReadPixels();

        // All pixels should be black
        int blackCount = 0;
        for (int y = 0; y < ScreenHeight; y++)
        {
            for (int x = 0; x < ScreenWidth; x++)
            {
                var (r, g, b, _) = ReadPixel(outputData, x, y, ScreenWidth);
                if (r < 0.01f && g < 0.01f && b < 0.01f)
                {
                    blackCount++;
                }
            }
        }

        Assert.True(blackCount == ScreenWidth * ScreenHeight,
            $"Mode 2 should show black for invalid probes, got {blackCount}/{ScreenWidth * ScreenHeight}");

        GL.DeleteProgram(programId);
    }

    #endregion

    #region Phase 5 Tests: Debug Shader Untested Modes

    /// <summary>
    /// Tests Mode 3 (Probe Normals) visualization.
    /// 
    /// DESIRED BEHAVIOR:
    /// - Renders probe normals as RGB color
    /// - Normal (0,0,-1) should appear as blueish
    /// 
    /// Setup:
    /// - Probes with forward-facing normals (0,0,-1)
    /// 
    /// Expected:
    /// - Output should show normal colors (encoded 0.5,0.5,0 for forward)
    /// </summary>
    [Fact]
    public void Mode3_ProbeNormals_ShowsNormalColors()
    {
        EnsureShaderTestAvailable();

        const float probeWorldZ = -5f;

        var depthData = CreateDepthBuffer(0.5f);
        var normalData = CreateNormalBuffer(0f, 0f, -1f);
        var anchorPosData = CreateProbeAnchors(probeWorldZ, validity: 1f);
        var anchorNormalData = CreateProbeNormals(0f, 0f, -1f);  // Forward-facing
        var radianceData = CreateUniformSHRadiance(0.5f, 0.5f, 0.5f);
        var historyMetaData = CreateHistoryMeta(5f, 0f, 0f, -1f, 1f);

        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
        using var radiance0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, radianceData);
        using var radiance1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, radianceData);
        using var indirectHalfTex = TestFramework.CreateTexture(HalfResWidth, HalfResHeight, PixelInternalFormat.Rgba16f, new float[HalfResWidth * HalfResHeight * 4]);
        using var historyMetaTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyMetaData);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ScreenWidth, ScreenHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileDebugShader();
        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupDebugUniforms(programId, debugMode: MODE_PROBE_NORMAL, identity, identity, identity);

        depthTex.Bind(0);
        normalTex.Bind(1);
        anchorPosTex.Bind(2);
        anchorNormalTex.Bind(3);
        radiance0Tex.Bind(4);
        radiance1Tex.Bind(5);
        indirectHalfTex.Bind(6);
        historyMetaTex.Bind(7);

        TestFramework.RenderQuadTo(programId, outputGBuffer);
        var outputData = outputGBuffer[0].ReadPixels();

        // Check that we get non-black output for valid probes
        int nonBlackCount = 0;
        for (int y = 0; y < ScreenHeight; y++)
        {
            for (int x = 0; x < ScreenWidth; x++)
            {
                var (r, g, b, _) = ReadPixel(outputData, x, y, ScreenWidth);
                if (r > 0.01f || g > 0.01f || b > 0.01f)
                    nonBlackCount++;
            }
        }

        Assert.True(nonBlackCount > 0,
            "Mode 3 should show normal colors for valid probes");

        GL.DeleteProgram(programId);
    }

    /// <summary>
    /// Tests Mode 6 (Temporal Weight) visualization.
    /// 
    /// DESIRED BEHAVIOR:
    /// - Shows the temporal blending weight as grayscale
    /// - Higher weight = more history influence
    /// 
    /// Setup:
    /// - Valid probes with consistent history
    /// 
    /// Expected:
    /// - Non-black output indicating temporal weight
    /// </summary>
    [Fact]
    public void Mode6_TemporalWeight_ShowsBlendFactor()
    {
        EnsureShaderTestAvailable();

        const float probeWorldZ = -5f;

        var depthData = CreateDepthBuffer(0.5f);
        var normalData = CreateNormalBuffer(0f, 0f, -1f);
        var anchorPosData = CreateProbeAnchors(probeWorldZ, validity: 1f);
        var anchorNormalData = CreateProbeNormals(0f, 0f, -1f);
        var radianceData = CreateUniformSHRadiance(0.5f, 0.5f, 0.5f);
        var historyMetaData = CreateHistoryMeta(-probeWorldZ, 0f, 0f, -1f, 0.9f);

        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
        using var radiance0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, radianceData);
        using var radiance1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, radianceData);
        using var indirectHalfTex = TestFramework.CreateTexture(HalfResWidth, HalfResHeight, PixelInternalFormat.Rgba16f, new float[HalfResWidth * HalfResHeight * 4]);
        using var historyMetaTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyMetaData);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ScreenWidth, ScreenHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileDebugShader();
        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupDebugUniforms(programId, debugMode: MODE_TEMPORAL_WEIGHT, identity, identity, identity);

        depthTex.Bind(0);
        normalTex.Bind(1);
        anchorPosTex.Bind(2);
        anchorNormalTex.Bind(3);
        radiance0Tex.Bind(4);
        radiance1Tex.Bind(5);
        indirectHalfTex.Bind(6);
        historyMetaTex.Bind(7);

        TestFramework.RenderQuadTo(programId, outputGBuffer);
        var outputData = outputGBuffer[0].ReadPixels();

        // Check for non-black output
        int nonBlackCount = 0;
        for (int y = 0; y < ScreenHeight; y++)
        {
            for (int x = 0; x < ScreenWidth; x++)
            {
                var (r, g, b, _) = ReadPixel(outputData, x, y, ScreenWidth);
                if (r > 0.01f || g > 0.01f || b > 0.01f)
                    nonBlackCount++;
            }
        }

        Assert.True(nonBlackCount > 0,
            "Mode 6 should visualize temporal weights");

        GL.DeleteProgram(programId);
    }

    /// <summary>
    /// Tests Mode 7 (Temporal Rejection) visualization.
    /// 
    /// DESIRED BEHAVIOR:
    /// - Shows rejection mask - red = rejected, green = accepted
    /// - Helps debug temporal stability issues
    /// 
    /// Setup:
    /// - Valid probes with matching history
    /// 
    /// Expected:
    /// - Non-black output showing rejection state
    /// </summary>
    [Fact]
    public void Mode7_TemporalRejection_ShowsRejectionMask()
    {
        EnsureShaderTestAvailable();

        const float probeWorldZ = -5f;

        var depthData = CreateDepthBuffer(0.5f);
        var normalData = CreateNormalBuffer(0f, 0f, -1f);
        var anchorPosData = CreateProbeAnchors(probeWorldZ, validity: 1f);
        var anchorNormalData = CreateProbeNormals(0f, 0f, -1f);
        var radianceData = CreateUniformSHRadiance(0.5f, 0.5f, 0.5f);
        var historyMetaData = CreateHistoryMeta(-probeWorldZ, 0f, 0f, -1f, 1f);

        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
        using var radiance0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, radianceData);
        using var radiance1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, radianceData);
        using var indirectHalfTex = TestFramework.CreateTexture(HalfResWidth, HalfResHeight, PixelInternalFormat.Rgba16f, new float[HalfResWidth * HalfResHeight * 4]);
        using var historyMetaTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyMetaData);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ScreenWidth, ScreenHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileDebugShader();
        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupDebugUniforms(programId, debugMode: MODE_TEMPORAL_REJECTION, identity, identity, identity);

        depthTex.Bind(0);
        normalTex.Bind(1);
        anchorPosTex.Bind(2);
        anchorNormalTex.Bind(3);
        radiance0Tex.Bind(4);
        radiance1Tex.Bind(5);
        indirectHalfTex.Bind(6);
        historyMetaTex.Bind(7);

        TestFramework.RenderQuadTo(programId, outputGBuffer);
        var outputData = outputGBuffer[0].ReadPixels();

        // Check for output (rejection visualization)
        int nonBlackCount = 0;
        for (int y = 0; y < ScreenHeight; y++)
        {
            for (int x = 0; x < ScreenWidth; x++)
            {
                var (r, g, b, _) = ReadPixel(outputData, x, y, ScreenWidth);
                if (r > 0.01f || g > 0.01f || b > 0.01f)
                    nonBlackCount++;
            }
        }

        Assert.True(nonBlackCount > 0,
            "Mode 7 should visualize temporal rejection mask");

        GL.DeleteProgram(programId);
    }

    /// <summary>
    /// Tests Mode 9 (Interpolation Weights) visualization.
    /// 
    /// DESIRED BEHAVIOR:
    /// - Shows bilinear interpolation weights for probe gather
    /// - Visualizes how much each probe contributes
    /// 
    /// Setup:
    /// - Valid probe grid
    /// 
    /// Expected:
    /// - Non-black output showing weight distribution
    /// </summary>
    [Fact]
    public void Mode9_InterpolationWeights_ShowsProbeContributions()
    {
        EnsureShaderTestAvailable();

        const float probeWorldZ = -5f;

        var depthData = CreateDepthBuffer(0.5f);
        var normalData = CreateNormalBuffer(0f, 0f, -1f);
        var anchorPosData = CreateProbeAnchors(probeWorldZ, validity: 1f);
        var anchorNormalData = CreateProbeNormals(0f, 0f, -1f);
        var radianceData = CreateUniformSHRadiance(0.5f, 0.5f, 0.5f);
        var historyMetaData = CreateHistoryMeta(-probeWorldZ, 0f, 0f, -1f, 1f);

        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, normalData);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
        using var radiance0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, radianceData);
        using var radiance1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, radianceData);
        using var indirectHalfTex = TestFramework.CreateTexture(HalfResWidth, HalfResHeight, PixelInternalFormat.Rgba16f, new float[HalfResWidth * HalfResHeight * 4]);
        using var historyMetaTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyMetaData);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ScreenWidth, ScreenHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileDebugShader();
        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupDebugUniforms(programId, debugMode: MODE_INTERPOLATION_WEIGHTS, identity, identity, identity);

        depthTex.Bind(0);
        normalTex.Bind(1);
        anchorPosTex.Bind(2);
        anchorNormalTex.Bind(3);
        radiance0Tex.Bind(4);
        radiance1Tex.Bind(5);
        indirectHalfTex.Bind(6);
        historyMetaTex.Bind(7);

        TestFramework.RenderQuadTo(programId, outputGBuffer);
        var outputData = outputGBuffer[0].ReadPixels();

        // Check for output (weight visualization)
        int nonBlackCount = 0;
        for (int y = 0; y < ScreenHeight; y++)
        {
            for (int x = 0; x < ScreenWidth; x++)
            {
                var (r, g, b, _) = ReadPixel(outputData, x, y, ScreenWidth);
                if (r > 0.01f || g > 0.01f || b > 0.01f)
                    nonBlackCount++;
            }
        }

        Assert.True(nonBlackCount > 0,
            "Mode 9 should visualize interpolation weights");

        GL.DeleteProgram(programId);
    }

    #endregion
}
