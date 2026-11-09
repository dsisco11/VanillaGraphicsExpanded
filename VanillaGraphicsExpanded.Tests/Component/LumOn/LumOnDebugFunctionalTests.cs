using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Rendering;
using Xunit;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.Numerics;
namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

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
[Trait("Category", "GpuIntegration")]
public class LumOnDebugFunctionalTests : LumOnShaderFunctionalTestBase
{
    #region Public API
    /// <summary>Uses the shared GPU context to execute debug visualization contract cases.</summary>
    public LumOnDebugFunctionalTests(HeadlessGLFixture fixture) : base(fixture) { }
    #region Probe and scene geometry views

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

        // Arrange: Prepare authored inputs, the shader variant, and its bound resources.

        const float probeWorldZ = -5f;

        var depthData = CreateDepthBuffer(0.5f);
        var normalData = CreateNormalBuffer(0f, 0f, -1f);
        var anchorPosData = CreateMixedValidityProbeAnchors(probeWorldZ);
        var anchorNormalData = CreateProbeNormals(0f, 0f, -1f);
        var radianceData = CreateUniformSHRadiance(1f, 1f, 1f);
        var historyMetaData = CreateHistoryMeta(5f, 0f, 0f, -1f, 1f);

        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, normalData);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorNormalData);
        using var radiance0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, radianceData);
        using var radiance1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, radianceData);
        using var indirectHalfTex = TestFramework.CreateTexture(HalfResWidth, HalfResHeight, TextureFormat.Rgba16f, new float[HalfResWidth * HalfResHeight * 4]);
        using var historyMetaTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, historyMetaData);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ScreenWidth, ScreenHeight,
            TextureFormat.Rgba16f);

        var programId = CompileDebugShader(MODE_PROBE_GRID);

        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupDebugUniforms(programId, debugMode: (int)MODE_PROBE_GRID, identity, identity, identity);

        programId.PrimaryDepth = depthTex;
        using var surfaceInput1 = LayeredTestTexture.Create(ImageAssertions.Programs, normalTex, null, null);
        programId.GBufferSurface = surfaceInput1;
        using var anchorInputs1 = LayeredTestTexture.Create(ImageAssertions.Programs, anchorPosTex, anchorNormalTex);
        programId.ProbeAnchors = anchorInputs1;
        programId.RadianceTexture0 = radiance0Tex;
        programId.RadianceTexture1 = radiance1Tex;
        programId.IndirectHalf = indirectHalfTex;
        programId.HistoryMeta = historyMetaTex;

        // Act: Render this shader configuration into the test target.
        TestFramework.RenderQuadTo(programId, outputGBuffer);
        var outputData = outputGBuffer[0];

        // Verify that some pixels have green (valid), yellow (edge), or red (invalid) colors
        // Assert: Read back the output and verify the case-specific behavior.
        bool hasColor=ImageAssertions.Predicates.Predicate(outputData, SurfacePredicates.LumOnDebugFunctionalTests_Mode1_ProbeGrid_ShowsValidityColors).MatchingTexels>0;
        // With mixed validity, we should see at least valid (green) and invalid (red)
        Assert.True(hasColor,
            "Mode 1 should show colored probe dots based on validity");
    }

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

        // Arrange: Prepare authored inputs, the shader variant, and its bound resources.

        const float probeWorldZ = -5f;

        var depthData = CreateDepthBuffer(0.5f);  // Mid-depth
        var normalData = CreateNormalBuffer(0f, 0f, -1f);
        var anchorPosData = CreateProbeAnchors(probeWorldZ);
        var anchorNormalData = CreateProbeNormals(0f, 0f, -1f);
        var radianceData = CreateUniformSHRadiance(1f, 1f, 1f);
        var historyMetaData = CreateHistoryMeta(5f, 0f, 0f, -1f, 1f);


        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, normalData);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorNormalData);
        using var radiance0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, radianceData);
        using var radiance1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, radianceData);
        using var indirectHalfTex = TestFramework.CreateTexture(HalfResWidth, HalfResHeight, TextureFormat.Rgba16f, new float[HalfResWidth * HalfResHeight * 4]);
        using var historyMetaTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, historyMetaData);
        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ScreenWidth, ScreenHeight,
            TextureFormat.Rgba16f);

        var programId = CompileDebugShader(MODE_SCENE_DEPTH);

        var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupDebugUniforms(programId, debugMode: (int)MODE_SCENE_DEPTH, invProjection, identity, identity);

        programId.PrimaryDepth = depthTex;
        using var surfaceInput2 = LayeredTestTexture.Create(ImageAssertions.Programs, normalTex, null, null);
        programId.GBufferSurface = surfaceInput2;
        using var anchorInputs2 = LayeredTestTexture.Create(ImageAssertions.Programs, anchorPosTex, anchorNormalTex);
        programId.ProbeAnchors = anchorInputs2;
        programId.RadianceTexture0 = radiance0Tex;
        programId.RadianceTexture1 = radiance1Tex;
        programId.IndirectHalf = indirectHalfTex;
        programId.HistoryMeta = historyMetaTex;

        // Act: Render this shader configuration into the test target.
        TestFramework.RenderQuadTo(programId, outputGBuffer);
        var outputData = outputGBuffer[0];

        // All pixels should have non-black color (heatmap)
        // Assert: Read back the output and verify the case-specific behavior.
        uint coloredCount=ImageAssertions.Predicates.Predicate(outputData, SurfacePredicates.LumOnDebugFunctionalTests_Mode4_SceneDepth_ShowsHeatmap).MatchingTexels;
        Assert.True(coloredCount == ScreenWidth * ScreenHeight,
            $"Mode 4 should show heatmap for all non-sky pixels, got {coloredCount}/{ScreenWidth * ScreenHeight}");
    }
    #endregion
    #region Scene and radiance views

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

        // Arrange: Prepare authored inputs, the shader variant, and its bound resources.

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

        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, normalData);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorNormalData);
        using var radiance0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, radianceData);
        using var radiance1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, radianceData);
        using var indirectHalfTex = TestFramework.CreateTexture(HalfResWidth, HalfResHeight, TextureFormat.Rgba16f, indirectData);

        using var historyMetaTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, historyMetaData);
        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ScreenWidth, ScreenHeight,
            TextureFormat.Rgba16f);

        var programId = CompileDebugShader(MODE_RADIANCE_OVERLAY);

        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupDebugUniforms(programId, debugMode: (int)MODE_RADIANCE_OVERLAY, identity, identity, identity);

        programId.PrimaryDepth = depthTex;
        using var surfaceInput3 = LayeredTestTexture.Create(ImageAssertions.Programs, normalTex, null, null);
        programId.GBufferSurface = surfaceInput3;
        using var anchorInputs3 = LayeredTestTexture.Create(ImageAssertions.Programs, anchorPosTex, anchorNormalTex);
        programId.ProbeAnchors = anchorInputs3;
        programId.RadianceTexture0 = radiance0Tex;
        programId.RadianceTexture1 = radiance1Tex;
        programId.IndirectHalf = indirectHalfTex;
        programId.HistoryMeta = historyMetaTex;

        // Act: Render this shader configuration into the test target.
        TestFramework.RenderQuadTo(programId, outputGBuffer);
        var outputData = outputGBuffer[0];

        // Reinhard tone map: 1/(1+1) = 0.5
        // Assert: Read back the output and verify the case-specific behavior.
        ImageAssertions.Predicates.Predicate(outputData, SurfacePredicates.LumOnDebugFunctionalTests_Mode10_RadianceOverlay_ShowsIndirectDiffuse).AssertEqual();
    }

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

        // Arrange: Prepare authored inputs, the shader variant, and its bound resources.

        const float probeWorldZ = -5f;

        var depthData = CreateDepthBuffer(1.0f);  // Sky
        var normalData = CreateNormalBuffer(0f, 0f, -1f);
        var anchorPosData = CreateProbeAnchors(probeWorldZ);
        var anchorNormalData = CreateProbeNormals(0f, 0f, -1f);
        var radianceData = CreateUniformSHRadiance(1f, 1f, 1f);
        var historyMetaData = CreateHistoryMeta(5f, 0f, 0f, -1f, 1f);


        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, normalData);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorNormalData);
        using var radiance0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, radianceData);
        using var radiance1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, radianceData);
        using var indirectHalfTex = TestFramework.CreateTexture(HalfResWidth, HalfResHeight, TextureFormat.Rgba16f, new float[HalfResWidth * HalfResHeight * 4]);
        using var historyMetaTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, historyMetaData);
        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ScreenWidth, ScreenHeight,
            TextureFormat.Rgba16f);

        var programId = CompileDebugShader(MODE_SCENE_DEPTH);

        var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupDebugUniforms(programId, debugMode: (int)MODE_SCENE_DEPTH, invProjection, identity, identity);

        programId.PrimaryDepth = depthTex;
        using var surfaceInput4 = LayeredTestTexture.Create(ImageAssertions.Programs, normalTex, null, null);
        programId.GBufferSurface = surfaceInput4;
        using var anchorInputs4 = LayeredTestTexture.Create(ImageAssertions.Programs, anchorPosTex, anchorNormalTex);
        programId.ProbeAnchors = anchorInputs4;
        programId.RadianceTexture0 = radiance0Tex;
        programId.RadianceTexture1 = radiance1Tex;
        programId.IndirectHalf = indirectHalfTex;
        programId.HistoryMeta = historyMetaTex;

        // Act: Render this shader configuration into the test target.
        TestFramework.RenderQuadTo(programId, outputGBuffer);
        var outputData = outputGBuffer[0];

        // All pixels should be black
        // Assert: Read back the output and verify the case-specific behavior.
        uint blackCount=ImageAssertions.Predicates.Predicate(outputData, SurfacePredicates.LumOnDebugFunctionalTests_Mode4_SceneDepth_SkyIsBlack).MatchingTexels;
        Assert.True(blackCount == ScreenWidth * ScreenHeight,
            $"Mode 4 should show black for sky, got {blackCount}/{ScreenWidth * ScreenHeight}");
    }

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

        // Arrange: Prepare authored inputs, the shader variant, and its bound resources.

        const float probeWorldZ = -5f;

        var depthData = CreateDepthBuffer(0.5f);
        var normalData = CreateNormalBuffer(0f, 1f, 0f);  // Upward
        var anchorPosData = CreateProbeAnchors(probeWorldZ);
        var anchorNormalData = CreateProbeNormals(0f, 1f, 0f);
        var radianceData = CreateUniformSHRadiance(1f, 1f, 1f);
        var historyMetaData = CreateHistoryMeta(5f, 0f, 1f, 0f, 1f);


        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, normalData);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorNormalData);
        using var radiance0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, radianceData);
        using var radiance1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, radianceData);
        using var indirectHalfTex = TestFramework.CreateTexture(HalfResWidth, HalfResHeight, TextureFormat.Rgba16f, new float[HalfResWidth * HalfResHeight * 4]);
        using var historyMetaTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, historyMetaData);
        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ScreenWidth, ScreenHeight,
            TextureFormat.Rgba16f);

        var programId = CompileDebugShader(MODE_SCENE_NORMAL);

        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupDebugUniforms(programId, debugMode: (int)MODE_SCENE_NORMAL, identity, identity, identity);

        programId.PrimaryDepth = depthTex;
        using var surfaceInput5 = LayeredTestTexture.Create(ImageAssertions.Programs, normalTex, null, null);
        programId.GBufferSurface = surfaceInput5;
        using var anchorInputs5 = LayeredTestTexture.Create(ImageAssertions.Programs, anchorPosTex, anchorNormalTex);
        programId.ProbeAnchors = anchorInputs5;
        programId.RadianceTexture0 = radiance0Tex;
        programId.RadianceTexture1 = radiance1Tex;
        programId.IndirectHalf = indirectHalfTex;
        programId.HistoryMeta = historyMetaTex;

        // Act: Render this shader configuration into the test target.
        TestFramework.RenderQuadTo(programId, outputGBuffer);
        var outputData = outputGBuffer[0];

        // Check that output shows upward normal color: (0.5, 1.0, 0.5)
        // Assert: Read back the output and verify the case-specific behavior.
        var (r, g, b, _) = ReadLocalPixel(outputData.ReadPixelsRegion(ScreenWidth/2,ScreenHeight/2,1,1));

        Assert.True(g > 0.8f,
            $"Mode 5 with upward normal should have high green component, got ({r:F3}, {g:F3}, {b:F3})");
    }

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

        // Arrange: Prepare authored inputs, the shader variant, and its bound resources.

        const float probeWorldZ = -5f;

        var depthData = CreateDepthBuffer(0.5f);
        var normalData = CreateNormalBuffer(0f, 0f, -1f);
        var anchorPosData = CreateProbeAnchors(probeWorldZ);
        var anchorNormalData = CreateProbeNormals(0f, 0f, -1f);
        var radianceData = CreateUniformSHRadiance(1f, 1f, 1f);
        var historyMetaData = CreateHistoryMeta(5f, 0f, 0f, -1f, 1f);


        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, normalData);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorNormalData);
        using var radiance0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, radianceData);
        using var radiance1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, radianceData);
        using var indirectHalfTex = TestFramework.CreateTexture(HalfResWidth, HalfResHeight, TextureFormat.Rgba16f, new float[HalfResWidth * HalfResHeight * 4]);
        using var historyMetaTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, historyMetaData);
        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ScreenWidth, ScreenHeight,
            TextureFormat.Rgba16f);

        var programId = CompileDebugShader(MODE_SH_COEFFICIENTS);

        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupDebugUniforms(programId, debugMode: (int)MODE_SH_COEFFICIENTS, identity, identity, identity);

        programId.PrimaryDepth = depthTex;
        using var surfaceInput6 = LayeredTestTexture.Create(ImageAssertions.Programs, normalTex, null, null);
        programId.GBufferSurface = surfaceInput6;
        using var anchorInputs6 = LayeredTestTexture.Create(ImageAssertions.Programs, anchorPosTex, anchorNormalTex);
        programId.ProbeAnchors = anchorInputs6;
        programId.RadianceTexture0 = radiance0Tex;
        programId.RadianceTexture1 = radiance1Tex;
        programId.IndirectHalf = indirectHalfTex;
        programId.HistoryMeta = historyMetaTex;

        // Act: Render this shader configuration into the test target.
        TestFramework.RenderQuadTo(programId, outputGBuffer);
        var outputData = outputGBuffer[0];

        // Should have some non-zero output from SH visualization
        // Assert: Read back the output and verify the case-specific behavior.
        uint nonZeroCount=ImageAssertions.Predicates.Predicate(outputData, SurfacePredicates.LumOnDebugFunctionalTests_Mode8_SHCoefficients_ShowsRadiance).MatchingTexels;
        Assert.True(nonZeroCount > 0,
            "Mode 8 should show non-zero SH visualization for valid probes");
    }
    #endregion
    #region Probe depth and normal views

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

        // Arrange: Prepare authored inputs, the shader variant, and its bound resources.

        const float probeWorldZ = -5f;

        var depthData = CreateDepthBuffer(0.5f);
        var normalData = CreateNormalBuffer(0f, 0f, -1f);
        var anchorPosData = CreateProbeAnchors(probeWorldZ, validity: 0f);  // Invalid
        var anchorNormalData = CreateProbeNormals(0f, 0f, -1f);
        var radianceData = CreateUniformSHRadiance(1f, 1f, 1f);
        var historyMetaData = CreateHistoryMeta(5f, 0f, 0f, -1f, 1f);


        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, normalData);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorNormalData);
        using var radiance0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, radianceData);
        using var radiance1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, radianceData);
        using var indirectHalfTex = TestFramework.CreateTexture(HalfResWidth, HalfResHeight, TextureFormat.Rgba16f, new float[HalfResWidth * HalfResHeight * 4]);
        using var historyMetaTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, historyMetaData);
        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ScreenWidth, ScreenHeight,
            TextureFormat.Rgba16f);

        var programId = CompileDebugShader(MODE_PROBE_DEPTH);

        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupDebugUniforms(programId, debugMode: (int)MODE_PROBE_DEPTH, identity, identity, identity);

        programId.PrimaryDepth = depthTex;
        using var surfaceInput7 = LayeredTestTexture.Create(ImageAssertions.Programs, normalTex, null, null);
        programId.GBufferSurface = surfaceInput7;
        using var anchorInputs7 = LayeredTestTexture.Create(ImageAssertions.Programs, anchorPosTex, anchorNormalTex);
        programId.ProbeAnchors = anchorInputs7;
        programId.RadianceTexture0 = radiance0Tex;
        programId.RadianceTexture1 = radiance1Tex;
        programId.IndirectHalf = indirectHalfTex;
        programId.HistoryMeta = historyMetaTex;

        // Act: Render this shader configuration into the test target.
        TestFramework.RenderQuadTo(programId, outputGBuffer);
        var outputData = outputGBuffer[0];

        // All pixels should be black
        // Assert: Read back the output and verify the case-specific behavior.
        uint blackCount=ImageAssertions.Predicates.Predicate(outputData, SurfacePredicates.LumOnDebugFunctionalTests_Mode4_SceneDepth_SkyIsBlack).MatchingTexels;
        Assert.True(blackCount == ScreenWidth * ScreenHeight,
            $"Mode 2 should show black for invalid probes, got {blackCount}/{ScreenWidth * ScreenHeight}");
    }

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

        // Arrange: Prepare authored inputs, the shader variant, and its bound resources.

        const float probeWorldZ = -5f;

        var depthData = CreateDepthBuffer(0.5f);
        var normalData = CreateNormalBuffer(0f, 0f, -1f);
        var anchorPosData = CreateProbeAnchors(probeWorldZ, validity: 1f);
        var anchorNormalData = CreateProbeNormals(0f, 0f, -1f);  // Forward-facing
        var radianceData = CreateUniformSHRadiance(0.5f, 0.5f, 0.5f);
        var historyMetaData = CreateHistoryMeta(5f, 0f, 0f, -1f, 1f);


        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, normalData);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorNormalData);
        using var radiance0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, radianceData);
        using var radiance1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, radianceData);
        using var indirectHalfTex = TestFramework.CreateTexture(HalfResWidth, HalfResHeight, TextureFormat.Rgba16f, new float[HalfResWidth * HalfResHeight * 4]);
        using var historyMetaTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, historyMetaData);
        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ScreenWidth, ScreenHeight,
            TextureFormat.Rgba16f);

        var programId = CompileDebugShader(MODE_PROBE_NORMAL);

        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupDebugUniforms(programId, debugMode: (int)MODE_PROBE_NORMAL, identity, identity, identity);

        programId.PrimaryDepth = depthTex;
        using var surfaceInput8 = LayeredTestTexture.Create(ImageAssertions.Programs, normalTex, null, null);
        programId.GBufferSurface = surfaceInput8;
        using var anchorInputs8 = LayeredTestTexture.Create(ImageAssertions.Programs, anchorPosTex, anchorNormalTex);
        programId.ProbeAnchors = anchorInputs8;
        programId.RadianceTexture0 = radiance0Tex;
        programId.RadianceTexture1 = radiance1Tex;
        programId.IndirectHalf = indirectHalfTex;
        programId.HistoryMeta = historyMetaTex;

        // Act: Render this shader configuration into the test target.
        TestFramework.RenderQuadTo(programId, outputGBuffer);
        var outputData = outputGBuffer[0];

        // Check that we get non-black output for valid probes
        // Assert: Read back the output and verify the case-specific behavior.
        uint nonBlackCount=ImageAssertions.Predicates.Predicate(outputData, SurfacePredicates.LumOnDebugFunctionalTests_Mode8_SHCoefficients_ShowsRadiance).MatchingTexels;
        Assert.True(nonBlackCount > 0,
            "Mode 3 should show normal colors for valid probes");
    }
    #endregion
    #region Temporal and interpolation views

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

        // Arrange: Prepare authored inputs, the shader variant, and its bound resources.

        const float probeWorldZ = -5f;

        var depthData = CreateDepthBuffer(0.5f);
        var normalData = CreateNormalBuffer(0f, 0f, -1f);
        var anchorPosData = CreateProbeAnchors(probeWorldZ, validity: 1f);
        var anchorNormalData = CreateProbeNormals(0f, 0f, -1f);
        var radianceData = CreateUniformSHRadiance(0.5f, 0.5f, 0.5f);
        var historyMetaData = CreateHistoryMeta(-probeWorldZ, 0f, 0f, -1f, 0.9f);


        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, normalData);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorNormalData);
        using var radiance0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, radianceData);
        using var radiance1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, radianceData);
        using var indirectHalfTex = TestFramework.CreateTexture(HalfResWidth, HalfResHeight, TextureFormat.Rgba16f, new float[HalfResWidth * HalfResHeight * 4]);
        using var historyMetaTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, historyMetaData);
        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ScreenWidth, ScreenHeight,
            TextureFormat.Rgba16f);

        var programId = CompileDebugShader(MODE_TEMPORAL_WEIGHT);

        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupDebugUniforms(programId, debugMode: (int)MODE_TEMPORAL_WEIGHT, identity, identity, identity);

        programId.PrimaryDepth = depthTex;
        using var surfaceInput9 = LayeredTestTexture.Create(ImageAssertions.Programs, normalTex, null, null);
        programId.GBufferSurface = surfaceInput9;
        using var anchorInputs9 = LayeredTestTexture.Create(ImageAssertions.Programs, anchorPosTex, anchorNormalTex);
        programId.ProbeAnchors = anchorInputs9;
        programId.RadianceTexture0 = radiance0Tex;
        programId.RadianceTexture1 = radiance1Tex;
        programId.IndirectHalf = indirectHalfTex;
        programId.HistoryMeta = historyMetaTex;

        // Act: Render this shader configuration into the test target.
        TestFramework.RenderQuadTo(programId, outputGBuffer);
        var outputData = outputGBuffer[0];

        // Check for non-black output
        // Assert: Read back the output and verify the case-specific behavior.
        uint nonBlackCount=ImageAssertions.Predicates.Predicate(outputData, SurfacePredicates.LumOnDebugFunctionalTests_Mode8_SHCoefficients_ShowsRadiance).MatchingTexels;
        Assert.True(nonBlackCount > 0,
            "Mode 6 should visualize temporal weights");
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

        // Arrange: Prepare authored inputs, the shader variant, and its bound resources.

        const float probeWorldZ = -5f;

        var depthData = CreateDepthBuffer(0.5f);
        var normalData = CreateNormalBuffer(0f, 0f, -1f);
        var anchorPosData = CreateProbeAnchors(probeWorldZ, validity: 1f);
        var anchorNormalData = CreateProbeNormals(0f, 0f, -1f);
        var radianceData = CreateUniformSHRadiance(0.5f, 0.5f, 0.5f);
        var historyMetaData = CreateHistoryMeta(-probeWorldZ, 0f, 0f, -1f, 1f);


        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, normalData);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorNormalData);
        using var radiance0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, radianceData);
        using var radiance1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, radianceData);
        using var indirectHalfTex = TestFramework.CreateTexture(HalfResWidth, HalfResHeight, TextureFormat.Rgba16f, new float[HalfResWidth * HalfResHeight * 4]);
        using var historyMetaTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, historyMetaData);
        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ScreenWidth, ScreenHeight,
            TextureFormat.Rgba16f);

        var programId = CompileDebugShader(MODE_TEMPORAL_REJECTION);

        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupDebugUniforms(programId, debugMode: (int)MODE_TEMPORAL_REJECTION, identity, identity, identity);

        programId.PrimaryDepth = depthTex;
        using var surfaceInput10 = LayeredTestTexture.Create(ImageAssertions.Programs, normalTex, null, null);
        programId.GBufferSurface = surfaceInput10;
        using var anchorInputs10 = LayeredTestTexture.Create(ImageAssertions.Programs, anchorPosTex, anchorNormalTex);
        programId.ProbeAnchors = anchorInputs10;
        programId.RadianceTexture0 = radiance0Tex;
        programId.RadianceTexture1 = radiance1Tex;
        programId.IndirectHalf = indirectHalfTex;
        programId.HistoryMeta = historyMetaTex;

        // Act: Render this shader configuration into the test target.
        TestFramework.RenderQuadTo(programId, outputGBuffer);
        var outputData = outputGBuffer[0];

        // Check for output (rejection visualization)
        // Assert: Read back the output and verify the case-specific behavior.
        uint nonBlackCount=ImageAssertions.Predicates.Predicate(outputData, SurfacePredicates.LumOnDebugFunctionalTests_Mode8_SHCoefficients_ShowsRadiance).MatchingTexels;
        Assert.True(nonBlackCount > 0,
            "Mode 7 should visualize temporal rejection mask");
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

        // Arrange: Prepare authored inputs, the shader variant, and its bound resources.

        const float probeWorldZ = -5f;

        var depthData = CreateDepthBuffer(0.5f);
        var normalData = CreateNormalBuffer(0f, 0f, -1f);
        var anchorPosData = CreateProbeAnchors(probeWorldZ, validity: 1f);
        var anchorNormalData = CreateProbeNormals(0f, 0f, -1f);
        var radianceData = CreateUniformSHRadiance(0.5f, 0.5f, 0.5f);
        var historyMetaData = CreateHistoryMeta(-probeWorldZ, 0f, 0f, -1f, 1f);


        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
        using var normalTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, normalData);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorNormalData);
        using var radiance0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, radianceData);
        using var radiance1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, radianceData);
        using var indirectHalfTex = TestFramework.CreateTexture(HalfResWidth, HalfResHeight, TextureFormat.Rgba16f, new float[HalfResWidth * HalfResHeight * 4]);
        using var historyMetaTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, historyMetaData);
        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ScreenWidth, ScreenHeight,
            TextureFormat.Rgba16f);

        var programId = CompileDebugShader(MODE_INTERPOLATION_WEIGHTS);

        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupDebugUniforms(programId, debugMode: (int)MODE_INTERPOLATION_WEIGHTS, identity, identity, identity);

        programId.PrimaryDepth = depthTex;
        using var surfaceInput11 = LayeredTestTexture.Create(ImageAssertions.Programs, normalTex, null, null);
        programId.GBufferSurface = surfaceInput11;
        using var anchorInputs11 = LayeredTestTexture.Create(ImageAssertions.Programs, anchorPosTex, anchorNormalTex);
        programId.ProbeAnchors = anchorInputs11;
        programId.RadianceTexture0 = radiance0Tex;
        programId.RadianceTexture1 = radiance1Tex;
        programId.IndirectHalf = indirectHalfTex;
        programId.HistoryMeta = historyMetaTex;

        // Act: Render this shader configuration into the test target.
        TestFramework.RenderQuadTo(programId, outputGBuffer);
        var outputData = outputGBuffer[0];

        // Check for output (weight visualization)
        // Assert: Read back the output and verify the case-specific behavior.
        uint nonBlackCount=ImageAssertions.Predicates.Predicate(outputData, SurfacePredicates.LumOnDebugFunctionalTests_Mode8_SHCoefficients_ShowsRadiance).MatchingTexels;
        Assert.True(nonBlackCount > 0,
            "Mode 9 should visualize interpolation weights");
    }


    #endregion
    #endregion
    #region Protected
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("layer", 0, 0);
        programs.Prepare("predicate-surface", 0, 0);
    }

    #endregion

    #region Private
    #region Debug mode configuration
// Debug mode constants
    private const LumOnDebugMode MODE_PROBE_GRID = LumOnDebugMode.ProbeGrid;

    private const LumOnDebugMode MODE_PROBE_DEPTH = LumOnDebugMode.ProbeDepth;

    private const LumOnDebugMode MODE_PROBE_NORMAL = LumOnDebugMode.ProbeNormal;

    private const LumOnDebugMode MODE_SCENE_DEPTH = LumOnDebugMode.SceneDepth;

    private const LumOnDebugMode MODE_SCENE_NORMAL = LumOnDebugMode.SceneNormal;

    private const LumOnDebugMode MODE_TEMPORAL_WEIGHT = LumOnDebugMode.TemporalWeight;

    private const LumOnDebugMode MODE_TEMPORAL_REJECTION = LumOnDebugMode.TemporalRejection;

    private const LumOnDebugMode MODE_SH_COEFFICIENTS = LumOnDebugMode.ShCoefficients;

    private const LumOnDebugMode MODE_INTERPOLATION_WEIGHTS = LumOnDebugMode.InterpolationWeights;

    private const LumOnDebugMode MODE_RADIANCE_OVERLAY = LumOnDebugMode.RadianceOverlay;

    /// <summary>
    /// Compiles and links the debug visualization shader.
    /// </summary>
    private LumOnDebugShaderProgram CompileDebugShader(LumOnDebugMode mode) => Programs.Create<LumOnDebugShaderProgram>(identity: LumOnDebugShaderProgramFamily.GetProgramName(mode));

    #endregion

    #region Uniform configuration
    /// <summary>
    /// Sets up common uniforms for the debug shader.
    /// </summary>
    private void SetupDebugUniforms(
        LumOnDebugShaderProgram programId,
        int debugMode,
        float[] invProjection,
        float[] invView,
        float[] prevViewProj,
        float temporalAlpha = 0.9f,
        float depthRejectThreshold = 0.1f,
        float normalRejectThreshold = 0.9f)
    {
        // Bind the frame state used by this debug view before checking its rendered output.
        UpdateAndBindLumOnFrameUbo(
            programId,
            invProjectionMatrix: invProjection,
            invViewMatrix: invView,
            prevViewProjMatrix: prevViewProj);

        programId.DebugMode = debugMode;
        programId.TemporalAlpha = temporalAlpha;
        programId.DepthRejectThreshold = depthRejectThreshold;
        programId.NormalRejectThreshold = normalRejectThreshold;
        programId.IndirectTint = new(1,1,1);
        programId.IndirectIntensity = 1;
        programId.DiffuseAOStrength = 1;
        programId.SpecularAOStrength = 1;
    }

    #endregion

    #region Probe input data
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

    #endregion

    #region Scene and temporal input data
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

    #endregion

    #region Pixel observation
    /// <summary>
    /// Decodes the single originally nominated output pixel.
    /// </summary>
    private static (float r, float g, float b, float a) ReadLocalPixel(float[] data)
    {
        Assert.Equal(4,data.Length);
        int idx=0;
        return (data[idx], data[idx + 1], data[idx + 2], data[idx + 3]);
    }

    #endregion
    #endregion

}
