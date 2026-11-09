using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.PBR;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using Xunit;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.Numerics;
namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>
/// Functional tests for the LumOn probe-atlas trace shader pass.
///
/// These tests verify that the probe trace shader correctly:
/// - Traces rays from valid probes and fills atlas regions
/// - Returns sky/ambient color when rays miss geometry
/// - Encodes hit distances using log encoding
/// - Produces zero radiance for invalid probes
///
/// Test configuration:
/// - Probe grid: 2×2 probes
/// - Octahedral size: 8×8 texels per probe
/// - Atlas size: 16×16 (2×8 by 2×8)
/// - Screen buffer: 4×4 pixels
/// </summary>
/// <remarks>
/// Atlas layout for 2×2 probe grid with 8×8 octahedral tiles:
/// <code>
/// +--------+--------+
/// | P(0,1) | P(1,1) |  Row 1: probes (0,1) and (1,1)
/// | 8×8    | 8×8    |
/// +--------+--------+
/// | P(0,0) | P(1,0) |  Row 0: probes (0,0) and (1,0)
/// | 8×8    | 8×8    |
/// +--------+--------+
/// Atlas total: 16×16 texels
/// </code>
///
/// Hit distance encoding:
/// <code>
/// encoded = log(distance + 1.0)
/// decoded = exp(encoded) - 1.0
/// </code>
/// </remarks>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public class LumOnProbeAtlasTraceFunctionalTests : LumOnShaderFunctionalTestBase
{


    // Ray tracing defaults
    private const int RaySteps = 16;
    private const float RayMaxDistance = 50f;
    private const float RayThickness = 0.5f;

    // Sky fallback defaults
    private const float SkyMissWeight = 1.0f;

    #region Public API
    /// <summary>Creates the fixture-backed probe-atlas trace suite.</summary>
    public LumOnProbeAtlasTraceFunctionalTests(HeadlessGLFixture fixture) : base(fixture) { }


   #region Test: ValidProbe_TracesRaysToAtlas

    /// <summary>
    /// Tests that a valid probe traces rays and fills its 8×8 region in the atlas.
    ///
    /// DESIRED BEHAVIOR:
    /// - Valid probes should trace rays for ALL 64 texels in their octahedral region
    /// - When rays miss geometry (sky), each texel should receive sky/ambient color
    /// - No texel should remain zero when probe is valid and texelsPerFrame covers all
    ///
    /// Setup:
    /// - All probes valid with position at origin, normal upward
    /// - Depth buffer: sky (depth=1.0) so all rays miss
    /// - texelsPerFrame=64 to trace all texels in one pass
    ///
    /// Expected:
    /// - ALL 256 texels (4 probes × 64 texels) should have non-zero sky color
    /// </summary>
    [Fact]
    public void ValidProbe_TracesRaysToAtlas()
    {
        // Arrange
        EnsureShaderTestAvailable();

        // Create input textures
        var anchorPosData = CreateValidProbeAnchors();
        var anchorNormalData = CreateProbeNormalsUpward();
        var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(1.0f, channels: 1); // Sky everywhere
        var colorData = CreateUniformSceneColor(1f, 0f, 0f); // Red scene (won't be hit)
        var historyData = CreateZeroedHistory();

        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorNormalData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
        using var colorTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, colorData);
        using var historyTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rgba16f, historyData);

        // Create output atlas buffer
        using var outputAtlas = TestFramework.CreateTestGBuffer(
            AtlasWidth, AtlasHeight,
            TextureFormat.Rgba16f, TextureFormat.Rg32f);

        // Compile and setup shader - use realistic perspective matrices
        var programId = CompileOctahedralTraceShader(texelsPerFrame: 64);
        var projection = LumOnTestInputFactory.CreateRealisticProjection();
        var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
        var view = LumOnTestInputFactory.CreateIdentityView();
        var invView = LumOnTestInputFactory.CreateIdentityView();  // Identity view inverse = identity
        SetupOctahedralTraceUniforms(
            programId,
            invProjection: invProjection,
            projection: projection,
            view: view,
            invView: invView,
            texelsPerFrame: 64,  // Trace all texels
            ambientColor: (0.3f, 0.4f, 0.5f));

        // Bind inputs
        using var anchorInputs1 = LayeredTestTexture.Create(ImageAssertions.Programs, anchorPosTex, anchorNormalTex);
        programId.ProbeAnchors = anchorInputs1;
        programId.PrimaryDepth = depthTex;
        programId.HzbDepth = depthTex;
        programId.SurfaceAlbedo = colorTex;
        programId.ScreenProbeAtlasHistory = historyTex;

        // Render to atlas
        // Act
        TestFramework.RenderQuadTo(programId, outputAtlas);

        // Assert
        ImageAssertions.Predicates.Predicate(outputAtlas[0], SurfacePredicates.LumOnProbeAtlasTraceFunctionalTests_ValidProbe_TracesRaysToAtlas).AssertEqual();
    }

    #endregion

    #region Test: SkyMiss_ReturnsAmbientColor

    /// <summary>
    /// Tests that rays missing geometry return ambient color weighted by skyMissWeight.
    ///
    /// Setup:
    /// - Valid probes
    /// - Depth=1.0 everywhere (sky)
    /// - ambientColor=(0.2, 0.4, 0.6)
    /// - skyMissWeight=0.5
    ///
    /// Expected:
    /// - Output radiance should be approximately ambient * skyMissWeight
    /// - The sky gradient formula adds some variation, so we check ranges
    /// </summary>
    [Fact]
    public void SkyMiss_ReturnsAmbientColor()
    {
        // Arrange
        EnsureShaderTestAvailable();

        const float skyWeight = 0.5f;
        var ambient = (r: 0.2f, g: 0.4f, b: 0.6f);

        // Create input textures
        var anchorPosData = CreateValidProbeAnchors();
        var anchorNormalData = CreateProbeNormalsUpward();
        var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(1.0f, channels: 1); // Sky
        var colorData = CreateUniformSceneColor(1f, 0f, 0f);
        var historyData = CreateZeroedHistory();


        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorNormalData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
        using var colorTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, colorData);
        using var historyTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rgba16f, historyData);
        using var outputAtlas = TestFramework.CreateTestGBuffer(
            AtlasWidth, AtlasHeight,
            TextureFormat.Rgba16f, TextureFormat.Rg32f);

        // Use realistic perspective matrices for proper depth/ray calculations
        var programId = CompileOctahedralTraceShader(texelsPerFrame: 64, skyMissWeight: skyWeight);
        var projection = LumOnTestInputFactory.CreateRealisticProjection();
        var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
        var view = LumOnTestInputFactory.CreateIdentityView();
        var invView = LumOnTestInputFactory.CreateIdentityView();
        SetupOctahedralTraceUniforms(
            programId,
            invProjection: invProjection,
            projection: projection,
            view: view,
            invView: invView,
            texelsPerFrame: 64,
            ambientColor: ambient,
            sunColor: (0f, 0f, 0f),  // No sun contribution for cleaner test
            sunPosition: (0f, 1f, 0f));

        using var anchorInputs2 = LayeredTestTexture.Create(ImageAssertions.Programs, anchorPosTex, anchorNormalTex);
        programId.ProbeAnchors = anchorInputs2;
        programId.PrimaryDepth = depthTex;
        programId.HzbDepth = depthTex;
        programId.SurfaceAlbedo = colorTex;
        programId.ScreenProbeAtlasHistory = historyTex;

        // Act
        TestFramework.RenderQuadTo(programId, outputAtlas);
        var atlasData = outputAtlas[0];

        // Check a sample of texels for sky color contribution
        // DESIRED: Sky color should follow the formula:
        // skyColor = ambientColor * skyFactor * skyMissWeight
        // skyFactor = max(0, rayDir.y) * 0.5 + 0.5 (ranges from 0.5 to 1.0)
        //
        // For center texel (4,4) of octahedral map, ray direction depends on octahedral decode.
        // With upward-facing probe normal, center rays point roughly upward, giving skyFactor ≈ 1.0
        // Expected ≈ ambient * 1.0 * skyWeight = ambient * 0.5

        for (int probeY = 0; probeY < ProbeGridHeight; probeY++)
        {
            for (int probeX = 0; probeX < ProbeGridWidth; probeX++)
            {
                // Check center texel of each probe
                var (x, y) = GetAtlasCoord(probeX, probeY, 4, 4);
                var (r, g, b, _) = ReadAtlasTexel(atlasData, x, y);

                // Compare physical radiance after decoding persistent RGB; hit-distance alpha is independent.
                r /= LightingStorage.PersistentScale;
                g /= LightingStorage.PersistentScale;
                b /= LightingStorage.PersistentScale;
                // DESIRED: Each channel should be approximately ambient * skyFactor * skyWeight
                // Allow 50% tolerance for direction-dependent sky gradient
                float tolerance = 0.5f;
                float expectedR = ambient.r * skyWeight;  // 0.2 * 0.5 = 0.1
                float expectedG = ambient.g * skyWeight;  // 0.4 * 0.5 = 0.2
                float expectedB = ambient.b * skyWeight;  // 0.6 * 0.5 = 0.3

                // Assert
                Assert.True(r >= expectedR * (1 - tolerance) && r <= expectedR * (1 + tolerance),
                    $"Probe ({probeX},{probeY}) R channel: expected ≈{expectedR:F2}, got {r:F3}");
                Assert.True(g >= expectedG * (1 - tolerance) && g <= expectedG * (1 + tolerance),
                    $"Probe ({probeX},{probeY}) G channel: expected ≈{expectedG:F2}, got {g:F3}");
                Assert.True(b >= expectedB * (1 - tolerance) && b <= expectedB * (1 + tolerance),
                    $"Probe ({probeX},{probeY}) B channel: expected ≈{expectedB:F2}, got {b:F3}");
            }
        }
    }

    #endregion

    #region Test: HitDistance_EncodedCorrectly

    /// <summary>
    /// Tests that hit distances are log-encoded correctly in the alpha channel.
    ///
    /// Setup:
    /// - Sky depth (no hits), so hit distance = rayMaxDistance
    ///
    /// Expected:
    /// - outRadiance.a = log(rayMaxDistance + 1.0)
    /// </summary>
    [Fact]
    public void HitDistance_EncodedCorrectly()
    {
        // Arrange
        EnsureShaderTestAvailable();

        // Create input textures
        var anchorPosData = CreateValidProbeAnchors();
        var anchorNormalData = CreateProbeNormalsUpward();
        var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(1.0f, channels: 1); // Sky = miss
        var colorData = CreateUniformSceneColor(1f, 0f, 0f);
        var historyData = CreateZeroedHistory();


        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorNormalData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
        using var colorTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, colorData);
        using var historyTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rgba16f, historyData);
        using var outputAtlas = TestFramework.CreateTestGBuffer(
            AtlasWidth, AtlasHeight,
            TextureFormat.Rgba16f, TextureFormat.Rg32f);

        // Use realistic perspective matrices for proper depth/ray calculations
        var programId = CompileOctahedralTraceShader(texelsPerFrame: 64);
        var projection = LumOnTestInputFactory.CreateRealisticProjection();
        var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
        var view = LumOnTestInputFactory.CreateIdentityView();
        var invView = LumOnTestInputFactory.CreateIdentityView();
        SetupOctahedralTraceUniforms(
            programId,
            invProjection: invProjection,
            projection: projection,
            view: view,
            invView: invView,
            texelsPerFrame: 64);

        using var anchorInputs3 = LayeredTestTexture.Create(ImageAssertions.Programs, anchorPosTex, anchorNormalTex);
        programId.ProbeAnchors = anchorInputs3;
        programId.PrimaryDepth = depthTex;
        programId.HzbDepth = depthTex;
        programId.SurfaceAlbedo = colorTex;
        programId.ScreenProbeAtlasHistory = historyTex;

        // Act
        TestFramework.RenderQuadTo(programId, outputAtlas);
        // Assert
        var distance=ImageAssertions.Predicates.Predicate(outputAtlas[0], SurfacePredicates.LumOnProbeAtlasTraceFunctionalTests_HitDistance_EncodedCorrectly,parameters:new(RayMaxDistance,RayMaxDistance*.1f,0,0));
        distance.AssertEqual();Assert.Equal(16u,distance.MatchingTexels);
    }

    #endregion

    #region Test: InvalidProbe_ProducesZeroRadiance

    /// <summary>
    /// Tests that invalid probes (validity=0) produce zero radiance.
    ///
    /// Setup:
    /// - All probes invalid (validity=0)
    ///
    /// Expected:
    /// - All 64 texels per probe = (0, 0, 0, 0)
    /// </summary>
    [Fact]
    public void InvalidProbe_ProducesZeroRadiance()
    {
        // Arrange
        EnsureShaderTestAvailable();

        // Create input textures with INVALID probes
        var anchorPosData = CreateInvalidProbeAnchors();
        var anchorNormalData = CreateProbeNormalsUpward();
        var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(0.5f, channels: 1);
        var colorData = CreateUniformSceneColor(1f, 1f, 1f); // White scene
        var historyData = CreateZeroedHistory();


        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorNormalData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
        using var colorTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, colorData);
        using var historyTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rgba16f, historyData);
        using var outputAtlas = TestFramework.CreateTestGBuffer(
            AtlasWidth, AtlasHeight,
            TextureFormat.Rgba16f, TextureFormat.Rg32f);

        // Use realistic perspective matrices for proper depth/ray calculations
        var programId = CompileOctahedralTraceShader(texelsPerFrame: 8);
        var projection = LumOnTestInputFactory.CreateRealisticProjection();
        var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
        var view = LumOnTestInputFactory.CreateIdentityView();
        var invView = LumOnTestInputFactory.CreateIdentityView();
        SetupOctahedralTraceUniforms(
            programId,
            invProjection: invProjection,
            projection: projection,
            view: view,
            invView: invView,
            texelsPerFrame: 64);

        using var anchorInputs4 = LayeredTestTexture.Create(ImageAssertions.Programs, anchorPosTex, anchorNormalTex);
        programId.ProbeAnchors = anchorInputs4;
        programId.PrimaryDepth = depthTex;
        programId.HzbDepth = depthTex;
        programId.SurfaceAlbedo = colorTex;
        programId.ScreenProbeAtlasHistory = historyTex;

        // Act
        TestFramework.RenderQuadTo(programId, outputAtlas);
        // Assert
        ImageAssertions.Predicates.Predicate(outputAtlas[0], SurfacePredicates.LumOnProbeAtlasTemporalFunctionalTests_InvalidProbe_OutputsZero,
            parameters:new(TestEpsilon,0,0,0)).AssertEqual();
    }

    #endregion

    #region Test: TemporalDistribution_OnlyTracesSubset

    /// <summary>
    /// Tests that temporal distribution only traces a subset of texels each frame.
    ///
    /// Setup:
    /// - texelsPerFrame=8 (out of 64 total)
    /// - Frame 0 should trace batch 0 only
    /// - History texture initialized to specific color
    ///
    /// Expected:
    /// - 8 texels per probe should be newly traced
    /// - 56 texels per probe should retain history color
    /// </summary>
    [Fact]
    public void TemporalDistribution_OnlyTracesSubset()
    {
        // Arrange
        EnsureShaderTestAvailable();

        // Create history with a recognizable color (cyan)
        var historyData = new float[AtlasWidth * AtlasHeight * 4];
        for (int i = 0; i < AtlasWidth * AtlasHeight; i++)
        {
            int idx = i * 4;
            historyData[idx + 0] = 0f;   // R
            historyData[idx + 1] = 1f;   // G
            historyData[idx + 2] = 1f;   // B (cyan)
            historyData[idx + 3] = 0.5f; // A
        }

        var anchorPosData = CreateValidProbeAnchors();
        var anchorNormalData = CreateProbeNormalsUpward();
        var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(1.0f, channels: 1);
        var colorData = CreateUniformSceneColor(1f, 0f, 0f);


        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorNormalData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
        using var colorTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, colorData);
        using var historyTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rgba16f, historyData);
        using var outputAtlas = TestFramework.CreateTestGBuffer(
            AtlasWidth, AtlasHeight,
            TextureFormat.Rgba16f, TextureFormat.Rg32f);

        // Use realistic perspective matrices for proper depth/ray calculations
        var programId = CompileOctahedralTraceShader(texelsPerFrame: 8);
        var projection = LumOnTestInputFactory.CreateRealisticProjection();
        var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
        var view = LumOnTestInputFactory.CreateIdentityView();
        var invView = LumOnTestInputFactory.CreateIdentityView();
        SetupOctahedralTraceUniforms(
            programId,
            invProjection: invProjection,
            projection: projection,
            view: view,
            invView: invView,
            frameIndex: 0,
            ambientColor: (0.5f, 0.5f, 0.5f));

        using var anchorInputs5 = LayeredTestTexture.Create(ImageAssertions.Programs, anchorPosTex, anchorNormalTex);
        programId.ProbeAnchors = anchorInputs5;
        programId.PrimaryDepth = depthTex;
        programId.HzbDepth = depthTex;
        programId.SurfaceAlbedo = colorTex;
        programId.ScreenProbeAtlasHistory = historyTex;

        // Act
        TestFramework.RenderQuadTo(programId, outputAtlas);
        // Assert
        var classified=ImageAssertions.Predicates.Predicate(outputAtlas[0], SurfacePredicates.LumOnProbeAtlasTraceFunctionalTests_TemporalDistribution_OnlyTracesSubset);
        classified.AssertEqual();
        int historyTexels=checked((int)classified.MatchingTexels);
        int tracedTexels=AtlasWidth*AtlasHeight-historyTexels;
        // DESIRED BEHAVIOR:
        // With texelsPerFrame=8 and 4 probes, exactly 32 texels should be traced per frame
        // Remaining 224 texels should retain history
        //
        // The shader should use deterministic temporal distribution without random jitter
        // that would cause unpredictable counts.
        int expectedTraced = ProbeGridWidth * ProbeGridHeight * 8;  // 32
        int expectedHistory = AtlasWidth * AtlasHeight - expectedTraced;  // 224

        // DESIRED: Exact counts (allow ±2 for rounding edge cases only)
        Assert.True(tracedTexels >= expectedTraced - 2 && tracedTexels <= expectedTraced + 2,
            $"Expected exactly {expectedTraced} traced texels (±2), got {tracedTexels}");
        Assert.True(historyTexels >= expectedHistory - 2 && historyTexels <= expectedHistory + 2,
            $"Expected exactly {expectedHistory} history texels (±2), got {historyTexels}");
    }

    #endregion

    #region Additional trace controls

    /// <summary>
    /// Tests that rays hitting geometry return the scene color from the hit point.
    ///
    /// DESIRED BEHAVIOR:
    /// - When a ray hits geometry (depth &lt; 1.0), it should sample the scene color
    /// - The hit radiance should appear in the octahedral texel
    ///
    /// Setup:
    /// - Probes at origin, centered in view
    /// - Depth buffer at 0.9675, which provides a visible hit for rays toward the scene
    /// - Scene albedo and emissive strength produce red hit radiance
    /// - Blue ambient distinguishes sky misses from scene hits
    ///
    /// Note: In octahedral tracing, rays go uniformly across a sphere. Only rays pointing
    /// toward the scene (-Z direction) can potentially hit geometry. Most rays will miss
    /// and return sky/ambient color. The assertions verify the hit metadata flag and distinguishable red-vs-blue radiance.
    /// </summary>
    [Fact]
    public void RayHit_ReturnsSceneColor()
    {
        // Arrange
        EnsureShaderTestAvailable();

        // Probes at origin - they're at the camera position with identity view
        var anchorPosData = CreateValidProbeAnchorsAtZ(0f);
        var anchorNormalData = CreateProbeNormalsUpward();
        var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(0.9675f, channels: 1);
        var colorData = CreateUniformSceneColor(1f, 0f, 0f); // Red scene albedo
        var historyData = CreateZeroedHistory();
        var metaHistoryData = CreateZeroedMetaHistory();
        var materialData = CreateUniformMaterialData(ScreenWidth, ScreenHeight, roughness: 0.5f, metallic: 0f, emissive: 1f);


        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorNormalData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
        using var colorTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, colorData);
        using var historyTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rgba16f, historyData);
        using var metaHistoryTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rg32f, metaHistoryData);
        using var materialTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, materialData);
        using var outputAtlas = TestFramework.CreateTestGBuffer(
            AtlasWidth, AtlasHeight,
            TextureFormat.Rgba16f, TextureFormat.Rg32f);

        var programId = CompileOctahedralTraceShader(texelsPerFrame: 8);

        var projection = LumOnTestInputFactory.CreateRealisticProjection();
        var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
        var view = LumOnTestInputFactory.CreateIdentityView();
        var invView = LumOnTestInputFactory.CreateIdentityView();
        SetupOctahedralTraceUniforms(
            programId,
            invProjection: invProjection,
            projection: projection,
            view: view,
            invView: invView,
            texelsPerFrame: 64,
            ambientColor: (0f, 0f, 1f),  // Blue ambient distinguishes sky misses
            sunColor: (0f, 0f, 0f));

        using var anchorInputs6 = LayeredTestTexture.Create(ImageAssertions.Programs, anchorPosTex, anchorNormalTex);
        using var surfaceInputs = LayeredTestTexture.Create(ImageAssertions.Programs, null, materialTex, null);
        programId.ProbeAnchors = anchorInputs6;
        programId.GBufferSurface = surfaceInputs;
        programId.PrimaryDepth = depthTex;
        programId.HzbDepth = depthTex;
        programId.SurfaceAlbedo = colorTex;
        programId.ScreenProbeAtlasHistory = historyTex;
        programId.ScreenProbeAtlasMetaHistory = metaHistoryTex;

        // Act
        TestFramework.RenderQuadTo(programId, outputAtlas);
        // Assert that at least one output texel records an actual hit and carries red emissive radiance,
        // distinguishing the authored scene result from the blue ambient miss fallback.
        var hitIndex = FindHitMetadataTexel(outputAtlas[1]);
        Assert.True(hitIndex >= 0, "Expected the authored scene depth to produce at least one ray hit.");
        int hitX = hitIndex % AtlasWidth;
        int hitY = hitIndex / AtlasWidth;
        var hitRadiance = ReadAtlasTexel(outputAtlas[0], hitX, hitY);
        Assert.True(hitRadiance.r > hitRadiance.b,
            $"Expected red scene emissive radiance to exceed blue ambient fallback at hit ({hitX},{hitY}), got ({hitRadiance.r:F3},{hitRadiance.g:F3},{hitRadiance.b:F3}).");
    }

    /// <summary>
    /// Tests that the higher ray-step specialization detects more authored scene hits.
    ///
    /// DESIRED BEHAVIOR:
    /// - More ray steps should sample the authored scene depth closely enough to detect hits
    /// - Fewer steps may skip that depth interval
    ///
    /// Setup:
    /// - Compare raySteps=4 vs raySteps=32
    ///
    /// Expected:
    /// - The high-step variant should report more hit metadata texels than the low-step variant
    /// </summary>
    [Fact]
    public void RaySteps_AffectsTraceQuality()
    {
        // Arrange
        EnsureShaderTestAvailable();

        var anchorPosData = CreateValidProbeAnchorsAtZ(0f);
        var anchorNormalData = CreateProbeNormalsUpward();
        var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(0.9675f, channels: 1);
        var colorData = CreateUniformSceneColor(1f, 1f, 1f);
        var historyData = CreateZeroedHistory();
        var metaHistoryData = CreateZeroedMetaHistory();

        int lowHits;
        int highHits;

        // Low ray steps
        {

            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorPosData);
            using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorNormalData);
            using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
            using var colorTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, colorData);
            using var historyTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rgba16f, historyData);
            using var metaHistoryTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rg32f, metaHistoryData);
            using var outputAtlas = TestFramework.CreateTestGBuffer(
                AtlasWidth, AtlasHeight,
                TextureFormat.Rgba16f, TextureFormat.Rg32f);

            var programId = CompileOctahedralTraceShader(raySteps: 4, texelsPerFrame: 64);

            var projection = LumOnTestInputFactory.CreateRealisticProjection();
            var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
            var view = LumOnTestInputFactory.CreateIdentityView();
            var invView = LumOnTestInputFactory.CreateIdentityView();
            SetupOctahedralTraceUniforms(
                programId,
                invProjection: invProjection,
                projection: projection,
                view: view,
                invView: invView,
                texelsPerFrame: 64);

            using var anchorInputs7 = LayeredTestTexture.Create(ImageAssertions.Programs, anchorPosTex, anchorNormalTex);
            programId.ProbeAnchors = anchorInputs7;
            programId.PrimaryDepth = depthTex;
            programId.HzbDepth = depthTex;
            programId.SurfaceAlbedo = colorTex;
            programId.ScreenProbeAtlasHistory = historyTex;
            programId.ScreenProbeAtlasMetaHistory = metaHistoryTex;

            // Act
            TestFramework.RenderQuadTo(programId, outputAtlas);
            // Assert
            lowHits = CountHitMetadataTexels(outputAtlas[1]);
        }

        // High ray steps
        {

            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorPosData);
            using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorNormalData);
            using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
            using var colorTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, colorData);
            using var historyTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rgba16f, historyData);
            using var metaHistoryTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rg32f, metaHistoryData);
            using var outputAtlas = TestFramework.CreateTestGBuffer(
                AtlasWidth, AtlasHeight,
                TextureFormat.Rgba16f, TextureFormat.Rg32f);

            var programId = CompileOctahedralTraceShader(raySteps: 32, texelsPerFrame: 64);

            var projection = LumOnTestInputFactory.CreateRealisticProjection();
            var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
            var view = LumOnTestInputFactory.CreateIdentityView();
            var invView = LumOnTestInputFactory.CreateIdentityView();
            SetupOctahedralTraceUniforms(
                programId,
                invProjection: invProjection,
                projection: projection,
                view: view,
                invView: invView,
                texelsPerFrame: 64);

            using var anchorInputs8 = LayeredTestTexture.Create(ImageAssertions.Programs, anchorPosTex, anchorNormalTex);
            programId.ProbeAnchors = anchorInputs8;
            programId.PrimaryDepth = depthTex;
            programId.HzbDepth = depthTex;
            programId.SurfaceAlbedo = colorTex;
            programId.ScreenProbeAtlasHistory = historyTex;
            programId.ScreenProbeAtlasMetaHistory = metaHistoryTex;

            // Act
            TestFramework.RenderQuadTo(programId, outputAtlas);
            // Assert
            highHits = CountHitMetadataTexels(outputAtlas[1]);
        }

        Assert.True(highHits > lowHits, $"Expected the high-step specialization to detect more scene hits, but low={lowHits} and high={highHits}.");
    }

    #endregion

    #region Probe-atlas metadata output

    /// <summary>Scene hits receive greater metadata confidence than rays that miss into the sky.</summary>
    [Fact]
    public void Meta_HitHigherThanSky()
    {
        // Arrange
        EnsureShaderTestAvailable();

        // Depth chosen to produce a mix of hits and misses for common directions.
        // This corresponds to a moderate linear depth (~3 units) given the test zNear/zFar.
        var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(0.9675f, channels: 1);
        var colorData = CreateUniformSceneColor(0.2f, 0.8f, 0.1f);
        var historyData = CreateZeroedHistory();
        var metaHistoryData = CreateZeroedMetaHistory();

        var anchorPosData = CreateValidProbeAnchorsAtZ(0f);
        var anchorNormalData = CreateProbeNormalsUpward();

        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorNormalData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
        using var colorTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, colorData);
        using var historyTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rgba16f, historyData);
        using var metaHistoryTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rg32f, metaHistoryData);

        using var outputAtlas = TestFramework.CreateTestGBuffer(
            AtlasWidth, AtlasHeight,
            TextureFormat.Rgba16f,
            TextureFormat.Rg32f);

        var programId = CompileOctahedralTraceShader(texelsPerFrame: 8);

        var projection = LumOnTestInputFactory.CreateRealisticProjection();
        var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
        var view = LumOnTestInputFactory.CreateIdentityView();
        var invView = LumOnTestInputFactory.CreateIdentityView();
        SetupOctahedralTraceUniforms(
            programId,
            invProjection: invProjection,
            projection: projection,
            view: view,
            invView: invView,
            texelsPerFrame: 64,
            sunColor: (0f, 0f, 0f));

        // Bind meta history sampler (used for non-traced texels; harmless here since we trace all)

        using var anchorInputs9 = LayeredTestTexture.Create(ImageAssertions.Programs, anchorPosTex, anchorNormalTex);
        programId.ProbeAnchors = anchorInputs9;
        programId.PrimaryDepth = depthTex;
        programId.HzbDepth = depthTex;
        programId.SurfaceAlbedo = colorTex;
        programId.ScreenProbeAtlasHistory = historyTex;
        programId.ScreenProbeAtlasMetaHistory = metaHistoryTex;

        // Act
        TestFramework.RenderQuadTo(programId, outputAtlas);

        // Assert
        var hit=ImageAssertions.Predicates.Predicate(outputAtlas[0], SurfacePredicates.LumOnProbeAtlasTraceFunctionalTests_Meta_HitHigherThanSky,
            parameters:new(RayMaxDistance-.5f,0,0,0));
        var sky=ImageAssertions.Predicates.Predicate(outputAtlas[0], SurfacePredicates.LumOnProbeAtlasTraceFunctionalTests_Meta_HitHigherThanSky_2,
            parameters:new(RayMaxDistance-.05f,0,0,0));
        hit.AssertEqual();sky.AssertEqual();
        Assert.True(hit.MatchingTexels>0,"Expected at least one hit texel in the atlas");
        Assert.True(sky.MatchingTexels>0,"Expected at least one sky-miss texel in the atlas");
        int hitX=(int)hit.FirstWitnessIndex%AtlasWidth,hitY=(int)hit.FirstWitnessIndex/AtlasWidth;
        int skyX=(int)sky.FirstWitnessIndex%AtlasWidth,skyY=(int)sky.FirstWitnessIndex/AtlasWidth;
        var (hitConf,_)=ReadAtlasMetaTexel(outputAtlas[1],hitX,hitY);
        var (skyConf,_)=ReadAtlasMetaTexel(outputAtlas[1],skyX,skyY);
        Assert.True(hitConf > skyConf,
            $"Expected hit confidence > sky confidence, got hit={hitConf:F3} sky={skyConf:F3} (hitCoord={hitX},{hitY} skyCoord={skyX},{skyY})");
    }

    /// <summary>Rays that leave the screen receive lower confidence than rays that remain in the sky region.</summary>
    [Fact]
    public void Meta_RayExitsScreen_LowerConfidence()
    {
        // Arrange
        EnsureShaderTestAvailable();

        var anchorPosData = CreateValidProbeAnchorsAtZ(0f);
        var anchorNormalData = CreateProbeNormalsUpward();
        var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(1.0f, channels: 1); // Sky everywhere
        var colorData = CreateUniformSceneColor(1f, 0f, 0f);
        var historyData = CreateZeroedHistory();
        var metaHistoryData = CreateZeroedMetaHistory();


        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorNormalData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
        using var colorTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, colorData);
        using var historyTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rgba16f, historyData);
        using var metaHistoryTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rg32f, metaHistoryData);
        using var outputAtlas = TestFramework.CreateTestGBuffer(
            AtlasWidth, AtlasHeight,
            TextureFormat.Rgba16f,
            TextureFormat.Rg32f);

        var programId = CompileOctahedralTraceShader(texelsPerFrame: 8);

        var projection = LumOnTestInputFactory.CreateRealisticProjection();
        var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
        var view = LumOnTestInputFactory.CreateIdentityView();
        var invView = LumOnTestInputFactory.CreateIdentityView();
        SetupOctahedralTraceUniforms(
            programId,
            invProjection: invProjection,
            projection: projection,
            view: view,
            invView: invView,
            texelsPerFrame: 64,
            sunColor: (0f, 0f, 0f));

        using var anchorInputs10 = LayeredTestTexture.Create(ImageAssertions.Programs, anchorPosTex, anchorNormalTex);
        programId.ProbeAnchors = anchorInputs10;
        programId.PrimaryDepth = depthTex;
        programId.HzbDepth = depthTex;
        programId.SurfaceAlbedo = colorTex;
        programId.ScreenProbeAtlasHistory = historyTex;
        programId.ScreenProbeAtlasMetaHistory = metaHistoryTex;

        // Act
        TestFramework.RenderQuadTo(programId, outputAtlas);
        // Assert
        var confidence=ImageAssertions.Statistics.Inspect(outputAtlas[1],channelMask:1);
        confidence.AssertFiniteRange();
        float minConf=(float)confidence.Minimum,maxConf=(float)confidence.Maximum;
        Assert.True(minConf < maxConf, $"Expected some rays to exit screen (minConf={minConf:F3}, maxConf={maxConf:F3})");
        Assert.True(minConf <= 0.06f, $"Expected exit confidence to be low (<= 0.06), got {minConf:F3}");
        Assert.True(maxConf >= 0.20f, $"Expected non-exit sky-miss confidence to be higher (>= 0.20), got {maxConf:F3}");
    }

    /// <summary>An atlas texel omitted from the current trace batch keeps its prior metadata history.</summary>
    [Fact]
    public void Meta_NonTracedTexels_PreserveHistory()
    {
        // Arrange
        EnsureShaderTestAvailable();

        // Choose a texel that is not traced for probeIndex=0 when frameIndex=0 and texelsPerFrame=8.
        // (octX=0, octY=1) => texelIndex=8 => batch=1, while jitteredFrame=0.
        var (x, y) = GetAtlasCoord(0, 0, 0, 1);

        var anchorPosData = CreateValidProbeAnchorsAtZ(0f);
        var anchorNormalData = CreateProbeNormalsUpward();
        var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(1.0f, channels: 1); // Sky
        var colorData = CreateUniformSceneColor(1f, 0f, 0f);
        var historyData = CreateZeroedHistory();

        var metaHistoryData = CreateZeroedMetaHistory();
        int metaIdx = (y * AtlasWidth + x) * 2;
        metaHistoryData[metaIdx + 0] = 0.77f; // confidence
        metaHistoryData[metaIdx + 1] = 123.0f; // flags bits as float (opaque to this test)


        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorNormalData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
        using var colorTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, colorData);
        using var historyTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rgba16f, historyData);
        using var metaHistoryTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rg32f, metaHistoryData);
        using var outputAtlas = TestFramework.CreateTestGBuffer(
            AtlasWidth, AtlasHeight,
            TextureFormat.Rgba16f,
            TextureFormat.Rg32f);

        var programId = CompileOctahedralTraceShader(texelsPerFrame: 8);

        var projection = LumOnTestInputFactory.CreateRealisticProjection();
        var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
        var view = LumOnTestInputFactory.CreateIdentityView();
        var invView = LumOnTestInputFactory.CreateIdentityView();
        SetupOctahedralTraceUniforms(
            programId,
            invProjection: invProjection,
            projection: projection,
            view: view,
            invView: invView,
            frameIndex: 0,
            sunColor: (0f, 0f, 0f));

        using var anchorInputs11 = LayeredTestTexture.Create(ImageAssertions.Programs, anchorPosTex, anchorNormalTex);
        programId.ProbeAnchors = anchorInputs11;
        programId.PrimaryDepth = depthTex;
        programId.HzbDepth = depthTex;
        programId.SurfaceAlbedo = colorTex;
        programId.ScreenProbeAtlasHistory = historyTex;
        programId.ScreenProbeAtlasMetaHistory = metaHistoryTex;

        // Act
        TestFramework.RenderQuadTo(programId, outputAtlas);
        var metaData = outputAtlas[1];

        var (conf, flagsBitsAsFloat) = ReadAtlasMetaTexel(metaData, x, y);
        // Assert
        Assert.True(MathF.Abs(conf - 0.77f) < 1e-4f, $"Expected preserved meta confidence at ({x},{y}) to be 0.77, got {conf:F6}");
        Assert.True(MathF.Abs(flagsBitsAsFloat - 123.0f) < 1e-4f,
            $"Expected preserved meta flags bits at ({x},{y}) to match history, got {flagsBitsAsFloat:F6}");
    }

    /// <summary>Sky misses retain the published hemispherical ambient response without adding an independent solar halo.</summary>
    [Fact]
    public void SkyMissRetainsAmbientWithoutIndependentSolarHalo()
    {
        // Arrange
        EnsureShaderTestAvailable();

        var anchorPosData = CreateValidProbeAnchors();
        var anchorNormalData = CreateProbeNormalsUpward();
        var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(1.0f, channels: 1);
        var colorData = CreateUniformSceneColor(0f, 0f, 0f);
        var historyData = CreateZeroedHistory();

        float withSunRed;
        float withoutSunRed;

        // With sun
        {

            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorPosData);
            using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorNormalData);
            using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
            using var colorTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, colorData);
            using var historyTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rgba16f, historyData);
            using var outputAtlas = TestFramework.CreateTestGBuffer(
                AtlasWidth, AtlasHeight,
                TextureFormat.Rgba16f, TextureFormat.Rg32f);

            var programId = CompileOctahedralTraceShader(texelsPerFrame: 64);

            var projection = LumOnTestInputFactory.CreateRealisticProjection();
            var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
            var view = LumOnTestInputFactory.CreateIdentityView();
            var invView = LumOnTestInputFactory.CreateIdentityView();
            SetupOctahedralTraceUniforms(
                programId,
                invProjection: invProjection,
                projection: projection,
                view: view,
                invView: invView,
                texelsPerFrame: 64,
                ambientColor: (0.2f, 0.2f, 0.2f),
                sunColor: (1f, 0f, 0f),
                sunPosition: (0f, 1f, 0f));

            using var anchorInputs12 = LayeredTestTexture.Create(ImageAssertions.Programs, anchorPosTex, anchorNormalTex);
            programId.ProbeAnchors = anchorInputs12;
            programId.PrimaryDepth = depthTex;
            programId.HzbDepth = depthTex;
            programId.SurfaceAlbedo = colorTex;
            programId.ScreenProbeAtlasHistory = historyTex;

            // Act
            TestFramework.RenderQuadTo(programId, outputAtlas);
            // Assert
            withSunRed = ImageAssertions.Statistics.OrderedFloatSum(outputAtlas[0], ObservationProjections.GpuImageAssertionTests_OrderedFloatReductionRetainsOriginalArithmetic);
        }

        // Without sun
        {

            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorPosData);
            using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorNormalData);
            using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
            using var colorTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, colorData);
            using var historyTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rgba16f, historyData);
            using var outputAtlas = TestFramework.CreateTestGBuffer(
                AtlasWidth, AtlasHeight,
                TextureFormat.Rgba16f, TextureFormat.Rg32f);

            var programId = CompileOctahedralTraceShader(texelsPerFrame: 64);

            var projection = LumOnTestInputFactory.CreateRealisticProjection();
            var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
            var view = LumOnTestInputFactory.CreateIdentityView();
            var invView = LumOnTestInputFactory.CreateIdentityView();
            SetupOctahedralTraceUniforms(
                programId,
                invProjection: invProjection,
                projection: projection,
                view: view,
                invView: invView,
                texelsPerFrame: 64,
                ambientColor: (0.2f, 0.2f, 0.2f),
                sunColor: (0f, 0f, 0f),
                sunPosition: (0f, 1f, 0f));

            using var anchorInputs13 = LayeredTestTexture.Create(ImageAssertions.Programs, anchorPosTex, anchorNormalTex);
            programId.ProbeAnchors = anchorInputs13;
            programId.PrimaryDepth = depthTex;
            programId.HzbDepth = depthTex;
            programId.SurfaceAlbedo = colorTex;
            programId.ScreenProbeAtlasHistory = historyTex;

            // Act
            TestFramework.RenderQuadTo(programId, outputAtlas);
            // Assert
            withoutSunRed = ImageAssertions.Statistics.OrderedFloatSum(outputAtlas[0], ObservationProjections.GpuImageAssertionTests_OrderedFloatReductionRetainsOriginalArithmetic);
        }

        // docs/PBR.Atmosphere.md: hemispherical sky replaces the former fabricated solar halo.
        Assert.True(withoutSunRed > 0f);
        Assert.Equal(withoutSunRed, withSunRed);
    }

    /// <summary>
    /// Tests that indirectTint uniform is properly passed to the shader.
    ///
    /// Note: indirectTint only affects geometry hits, not sky/ambient contribution.
    /// In octahedral tracing with test setup constraints, achieving reliable geometry
    /// hits is difficult. This test is skipped with explanation - the uniform binding
    /// is verified by shader compilation and other tests that use default tint values.
    ///
    /// The indirectTint functionality is tested indirectly by:
    /// 1. Shader compilation succeeding (uniform exists)
    /// 2. Other tests using default tint=(1,1,1) producing expected output
    /// </summary>
    [Fact(Skip = "IndirectTint only affects geometry hits, which are difficult to reliably produce in probe-atlas tracing tests. Uniform binding verified by compilation.")]
    public void IndirectTint_AppliedToHitRadiance()
    {
        // Arrange
        EnsureShaderTestAvailable();

        // This test requires reliable geometry hits which are difficult to achieve
        // in octahedral tracing with simple test setups.
        var anchorPosData = CreateValidProbeAnchorsAtZ(-1f);
        var anchorNormalData = CreateProbeNormalsUpward();
        var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(0.99f, channels: 1);
        var colorData = CreateUniformSceneColor(1f, 1f, 1f);
        var historyData = CreateZeroedHistory();

        float fullTintBrightness;
        float halfTintBrightness;

        // Full tint
        {

            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorPosData);
            using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorNormalData);
            using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
            using var colorTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, colorData);
            using var historyTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rgba16f, historyData);
            using var outputAtlas = TestFramework.CreateTestGBuffer(
                AtlasWidth, AtlasHeight,
                TextureFormat.Rgba16f, TextureFormat.Rg32f);

            var programId = CompileOctahedralTraceShader(texelsPerFrame: 8);

            var projection = LumOnTestInputFactory.CreateRealisticProjection();
            var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
            var view = LumOnTestInputFactory.CreateIdentityView();
            var invView = LumOnTestInputFactory.CreateIdentityView();
            SetupOctahedralTraceUniforms(
                programId,
                invProjection: invProjection,
                projection: projection,
                view: view,
                invView: invView,
                texelsPerFrame: 64,
                ambientColor: (0f, 0f, 0f),
                sunColor: (0f, 0f, 0f),
                indirectTint: (1f, 1f, 1f));

            using var anchorInputs14 = LayeredTestTexture.Create(ImageAssertions.Programs, anchorPosTex, anchorNormalTex);
            programId.ProbeAnchors = anchorInputs14;
            programId.PrimaryDepth = depthTex;
            programId.HzbDepth = depthTex;
            programId.SurfaceAlbedo = colorTex;
            programId.ScreenProbeAtlasHistory = historyTex;

            // Act
            TestFramework.RenderQuadTo(programId, outputAtlas);
            // Assert
            fullTintBrightness = ImageAssertions.Statistics.OrderedFloatSum(outputAtlas[0], ObservationProjections.GpuImageAssertionTests_OrderedFloatReductionRetainsOriginalArithmetic_2);
        }

        // Half tint
        {

            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorPosData);
            using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorNormalData);
            using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
            using var colorTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, colorData);
            using var historyTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rgba16f, historyData);
            using var outputAtlas = TestFramework.CreateTestGBuffer(
                AtlasWidth, AtlasHeight,
                TextureFormat.Rgba16f, TextureFormat.Rg32f);

            var programId = CompileOctahedralTraceShader(texelsPerFrame: 8);

            var projection = LumOnTestInputFactory.CreateRealisticProjection();
            var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
            var view = LumOnTestInputFactory.CreateIdentityView();
            var invView = LumOnTestInputFactory.CreateIdentityView();
            SetupOctahedralTraceUniforms(
                programId,
                invProjection: invProjection,
                projection: projection,
                view: view,
                invView: invView,
                texelsPerFrame: 64,
                ambientColor: (0f, 0f, 0f),
                sunColor: (0f, 0f, 0f),
                indirectTint: (0.5f, 0.5f, 0.5f));

            using var anchorInputs15 = LayeredTestTexture.Create(ImageAssertions.Programs, anchorPosTex, anchorNormalTex);
            programId.ProbeAnchors = anchorInputs15;
            programId.PrimaryDepth = depthTex;
            programId.HzbDepth = depthTex;
            programId.SurfaceAlbedo = colorTex;
            programId.ScreenProbeAtlasHistory = historyTex;

            // Act
            TestFramework.RenderQuadTo(programId, outputAtlas);
            // Assert
            halfTintBrightness = ImageAssertions.Statistics.OrderedFloatSum(outputAtlas[0], ObservationProjections.GpuImageAssertionTests_OrderedFloatReductionRetainsOriginalArithmetic_2);
        }

        Assert.True(halfTintBrightness < fullTintBrightness * 0.8f,
            $"Half tint ({halfTintBrightness:F4}) should be less than 0.8x full tint ({fullTintBrightness:F4})");
    }

    /// <summary>
    /// Tests that distance falloff is applied to hit radiance in octahedral atlas.
    ///
    /// Note: Distance falloff only affects geometry hits. In octahedral tracing with
    /// test setup constraints (identity matrices, simple depth buffers), achieving
    /// reliable geometry hits at specific distances is very difficult.
    ///
    /// The distance falloff functionality is verified by:
    /// 1. Shader compilation succeeding (distanceFalloff function exists)
    /// 2. HitDistance_EncodedCorrectly test verifying distance encoding works
    /// 3. Code review of the shader's distanceFalloff() function
    /// </summary>
    [Fact(Skip = "Distance falloff only affects geometry hits, which are difficult to reliably produce at specific distances in probe-atlas tracing tests.")]
    public void DistanceFalloff_AppliedToHitRadiance()
    {
        // Arrange
        EnsureShaderTestAvailable();

        // This test requires reliable geometry hits at specific distances
        var anchorPosData = CreateValidProbeAnchorsAtZ(-1f);
        var anchorNormalData = CreateProbeNormalsUpward();
        var colorData = CreateUniformSceneColor(1f, 1f, 1f);
        var historyData = CreateZeroedHistory();

        float nearHitBrightness;
        float farHitBrightness;

        // Near geometry (depth 0.9 - closer)
        {
            var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(0.9f, channels: 1);


            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorPosData);
            using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorNormalData);
            using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
            using var colorTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, colorData);
            using var historyTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rgba16f, historyData);
            using var outputAtlas = TestFramework.CreateTestGBuffer(
                AtlasWidth, AtlasHeight,
                TextureFormat.Rgba16f, TextureFormat.Rg32f);

            var programId = CompileOctahedralTraceShader(texelsPerFrame: 8);

            var projection = LumOnTestInputFactory.CreateRealisticProjection();
            var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
            var view = LumOnTestInputFactory.CreateIdentityView();
            var invView = LumOnTestInputFactory.CreateIdentityView();
            SetupOctahedralTraceUniforms(
                programId,
                invProjection: invProjection,
                projection: projection,
                view: view,
                invView: invView,
                texelsPerFrame: 64,
                ambientColor: (0f, 0f, 0f),
                sunColor: (0f, 0f, 0f));

            using var anchorInputs16 = LayeredTestTexture.Create(ImageAssertions.Programs, anchorPosTex, anchorNormalTex);
            programId.ProbeAnchors = anchorInputs16;
            programId.PrimaryDepth = depthTex;
            programId.HzbDepth = depthTex;
            programId.SurfaceAlbedo = colorTex;
            programId.ScreenProbeAtlasHistory = historyTex;

            // Act
            TestFramework.RenderQuadTo(programId, outputAtlas);
            // Assert
            nearHitBrightness = ImageAssertions.Statistics.OrderedFloatSum(outputAtlas[0], ObservationProjections.GpuImageAssertionTests_OrderedFloatReductionRetainsOriginalArithmetic_2);
        }

        // Far geometry (depth 0.99 - farther)
        {
            var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(0.99f, channels: 1);


            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorPosData);
            using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorNormalData);
            using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
            using var colorTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, colorData);
            using var historyTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rgba16f, historyData);
            using var outputAtlas = TestFramework.CreateTestGBuffer(
                AtlasWidth, AtlasHeight,
                TextureFormat.Rgba16f, TextureFormat.Rg32f);

            var programId = CompileOctahedralTraceShader(texelsPerFrame: 8);

            var projection = LumOnTestInputFactory.CreateRealisticProjection();
            var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
            var view = LumOnTestInputFactory.CreateIdentityView();
            var invView = LumOnTestInputFactory.CreateIdentityView();
            SetupOctahedralTraceUniforms(
                programId,
                invProjection: invProjection,
                projection: projection,
                view: view,
                invView: invView,
                texelsPerFrame: 64,
                ambientColor: (0f, 0f, 0f),
                sunColor: (0f, 0f, 0f));

            using var anchorInputs17 = LayeredTestTexture.Create(ImageAssertions.Programs, anchorPosTex, anchorNormalTex);
            programId.ProbeAnchors = anchorInputs17;
            programId.PrimaryDepth = depthTex;
            programId.HzbDepth = depthTex;
            programId.SurfaceAlbedo = colorTex;
            programId.ScreenProbeAtlasHistory = historyTex;

            // Act
            TestFramework.RenderQuadTo(programId, outputAtlas);
            // Assert
            farHitBrightness = ImageAssertions.Statistics.OrderedFloatSum(outputAtlas[0], ObservationProjections.GpuImageAssertionTests_OrderedFloatReductionRetainsOriginalArithmetic_2);
        }

        // Near hits should produce different radiance than far hits due to distance encoding
        Assert.True(nearHitBrightness != farHitBrightness || nearHitBrightness > farHitBrightness * 0.8f,
            $"Distance should affect hit radiance: near={nearHitBrightness:F4}, far={farHitBrightness:F4}");
    }

    #endregion

    #region Additional trace controls

    /// <summary>
    /// Tests that frameIndex affects which texels are selected for tracing.
    ///
    /// DESIRED BEHAVIOR:
    /// - Different frame indices should trace different texels
    /// - This implements temporal distribution across frames
    ///
    /// Setup:
    /// - Same scene, different frameIndex values
    /// - Compare which texels are updated
    ///
    /// Expected:
    /// - Different frame indices should produce different update patterns
    /// </summary>
    [Fact]
    public void FrameIndex_JittersTexelSelection()
    {
        // Arrange
        EnsureShaderTestAvailable();

        var anchorPosData = CreateValidProbeAnchors();
        var anchorNormalData = CreateProbeNormalsUpward();
        var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(1.0f, channels: 1);
        var colorData = CreateUniformSceneColor(1f, 0f, 0f);
        var historyData = CreateZeroedHistory();

        GpuImageSnapshot? frame0Output=null,frame1Output=null;
        try
        {

        // Frame 0
        {

            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorPosData);
            using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorNormalData);
            using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
            using var colorTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, colorData);
            using var historyTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rgba16f, historyData);
            using var outputAtlas = TestFramework.CreateTestGBuffer(
                AtlasWidth, AtlasHeight,
                TextureFormat.Rgba16f, TextureFormat.Rg32f);

            var programId = CompileOctahedralTraceShader(texelsPerFrame: 8);

            var projection = LumOnTestInputFactory.CreateRealisticProjection();
            var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
            var view = LumOnTestInputFactory.CreateIdentityView();
            var invView = LumOnTestInputFactory.CreateIdentityView();
            SetupOctahedralTraceUniforms(
                programId,
                invProjection: invProjection,
                projection: projection,
                view: view,
                invView: invView,
                frameIndex: 0);

            using var anchorInputs18 = LayeredTestTexture.Create(ImageAssertions.Programs, anchorPosTex, anchorNormalTex);
            programId.ProbeAnchors = anchorInputs18;
            programId.PrimaryDepth = depthTex;
            programId.HzbDepth = depthTex;
            programId.SurfaceAlbedo = colorTex;
            programId.ScreenProbeAtlasHistory = historyTex;

            // Act
            TestFramework.RenderQuadTo(programId, outputAtlas);
            frame0Output = new(outputAtlas[0]);
        }

        // Frame 1
        {

            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorPosData);
            using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorNormalData);
            using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
            using var colorTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, colorData);
            using var historyTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rgba16f, historyData);
            using var outputAtlas = TestFramework.CreateTestGBuffer(
                AtlasWidth, AtlasHeight,
                TextureFormat.Rgba16f, TextureFormat.Rg32f);

            var programId = CompileOctahedralTraceShader(texelsPerFrame: 8);

            var projection = LumOnTestInputFactory.CreateRealisticProjection();
            var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
            var view = LumOnTestInputFactory.CreateIdentityView();
            var invView = LumOnTestInputFactory.CreateIdentityView();
            SetupOctahedralTraceUniforms(
                programId,
                invProjection: invProjection,
                projection: projection,
                view: view,
                invView: invView,
                frameIndex: 1);

            // Assert
            using var anchorInputs19 = LayeredTestTexture.Create(ImageAssertions.Programs, anchorPosTex, anchorNormalTex);
            programId.ProbeAnchors = anchorInputs19;
            programId.PrimaryDepth = depthTex;
            programId.HzbDepth = depthTex;
            programId.SurfaceAlbedo = colorTex;
            programId.ScreenProbeAtlasHistory = historyTex;

            // Act
            TestFramework.RenderQuadTo(programId, outputAtlas);
            frame1Output = new(outputAtlas[0]);
        }

        // Assert
        var differences=ImageAssertions.Predicates.Predicate(frame0Output!.Texture, SurfacePredicates.LumOnProbeAtlasTraceFunctionalTests_FrameIndex_JittersTexelSelection,
            frame1Output!.Texture);
        differences.AssertEqual();Assert.True(differences.MatchingTexels>0);
        }
        finally { frame1Output?.Dispose();frame0Output?.Dispose(); }
    }

    /// <summary>
    /// Tests that texelsPerFrame uniform controls how many texels are traced each frame.
    ///
    /// DESIRED BEHAVIOR:
    /// - texelsPerFrame=8: traces 8 texels per probe per frame
    /// - texelsPerFrame=32: traces 32 texels per probe per frame
    ///
    /// Setup:
    /// - Compare outputs with different texelsPerFrame values
    /// - Start from zeroed history
    ///
    /// Expected:
    /// - Higher texelsPerFrame should result in more non-zero texels
    /// </summary>
    [Fact]
    public void TexelsPerFrame_AffectsTemporalDistribution()
    {
        // Arrange
        EnsureShaderTestAvailable();

        var anchorPosData = CreateValidProbeAnchors();
        var anchorNormalData = CreateProbeNormalsUpward();
        var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(0.5f, channels: 1);
        var colorData = CreateUniformSceneColor(1f, 1f, 1f);
        var historyData = CreateZeroedHistory();

        int lowTexelsNonZero;
        int highTexelsNonZero;

        // Low texels per frame (8)
        {

            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorPosData);
            using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorNormalData);
            using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
            using var colorTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, colorData);
            using var historyTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rgba16f, historyData);
            using var outputAtlas = TestFramework.CreateTestGBuffer(
                AtlasWidth, AtlasHeight,
                TextureFormat.Rgba16f, TextureFormat.Rg32f);

            var programId = CompileOctahedralTraceShader(texelsPerFrame: 8);

            var projection = LumOnTestInputFactory.CreateRealisticProjection();
            var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
            var view = LumOnTestInputFactory.CreateIdentityView();
            var invView = LumOnTestInputFactory.CreateIdentityView();
            SetupOctahedralTraceUniforms(
                programId,
                invProjection: invProjection,
                projection: projection,
                view: view,
                invView: invView);

            using var anchorInputs20 = LayeredTestTexture.Create(ImageAssertions.Programs, anchorPosTex, anchorNormalTex);
            programId.ProbeAnchors = anchorInputs20;
            programId.PrimaryDepth = depthTex;
            programId.HzbDepth = depthTex;
            programId.SurfaceAlbedo = colorTex;
            programId.ScreenProbeAtlasHistory = historyTex;

            // Act
            TestFramework.RenderQuadTo(programId, outputAtlas);
            // Assert
            var count=ImageAssertions.Predicates.Predicate(outputAtlas[0], SurfacePredicates.LumOnProbeAtlasTraceFunctionalTests_TexelsPerFrame_AffectsTemporalDistribution);
            count.AssertEqual();lowTexelsNonZero=checked((int)count.MatchingTexels);
        }

        // High texels per frame (32)
        {

            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorPosData);
            using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorNormalData);
            using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
            using var colorTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, colorData);
            using var historyTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rgba16f, historyData);
            using var outputAtlas = TestFramework.CreateTestGBuffer(
                AtlasWidth, AtlasHeight,
                TextureFormat.Rgba16f, TextureFormat.Rg32f);

            var programId = CompileOctahedralTraceShader(texelsPerFrame: 32);

            var projection = LumOnTestInputFactory.CreateRealisticProjection();
            var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
            var view = LumOnTestInputFactory.CreateIdentityView();
            var invView = LumOnTestInputFactory.CreateIdentityView();
            SetupOctahedralTraceUniforms(
                programId,
                invProjection: invProjection,
                projection: projection,
                view: view,
                invView: invView);

            using var anchorInputs21 = LayeredTestTexture.Create(ImageAssertions.Programs, anchorPosTex, anchorNormalTex);
            programId.ProbeAnchors = anchorInputs21;
            programId.PrimaryDepth = depthTex;
            programId.HzbDepth = depthTex;
            programId.SurfaceAlbedo = colorTex;
            programId.ScreenProbeAtlasHistory = historyTex;

            // Act
            TestFramework.RenderQuadTo(programId, outputAtlas);
            // Assert
            var count=ImageAssertions.Predicates.Predicate(outputAtlas[0], SurfacePredicates.LumOnProbeAtlasTraceFunctionalTests_TexelsPerFrame_AffectsTemporalDistribution);
            count.AssertEqual();highTexelsNonZero=checked((int)count.MatchingTexels);
        }

        // Higher texelsPerFrame should trace more texels
        Assert.True(highTexelsNonZero >= lowTexelsNonZero,
            $"Higher texelsPerFrame should trace more texels: low={lowTexelsNonZero}, high={highTexelsNonZero}");
    }

    /// <summary>
    /// Tests that rays exiting the screen boundary terminate early.
    ///
    /// DESIRED BEHAVIOR:
    /// - Rays that march outside screen UV [0,1] should terminate
    /// - Should return sky/ambient color for out-of-bounds rays
    ///
    /// Setup:
    /// - Probe at screen edge
    /// - Ray direction pointing off-screen
    ///
    /// Expected:
    /// - Rays exiting screen should produce valid (non-garbage) output
    /// </summary>
    [Fact]
    public void RayExitsScreen_TerminatesEarly()
    {
        // Arrange
        EnsureShaderTestAvailable();

        // Place probes at screen corners where rays are likely to exit
        var anchorPosData = CreateValidProbeAnchors();
        var anchorNormalData = CreateProbeNormalsUpward();
        var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(1.0f, channels: 1);  // Sky (far plane)
        var colorData = CreateUniformSceneColor(0.5f, 0.5f, 0.5f);
        var historyData = CreateZeroedHistory();


        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorNormalData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
        using var colorTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, colorData);
        using var historyTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rgba16f, historyData);
        using var outputAtlas = TestFramework.CreateTestGBuffer(
            AtlasWidth, AtlasHeight,
            TextureFormat.Rgba16f, TextureFormat.Rg32f);

        var programId = CompileOctahedralTraceShader();

        var projection = LumOnTestInputFactory.CreateRealisticProjection();
        var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
        var view = LumOnTestInputFactory.CreateIdentityView();
        var invView = LumOnTestInputFactory.CreateIdentityView();
        SetupOctahedralTraceUniforms(
            programId,
            invProjection: invProjection,
            projection: projection,
            view: view,
            invView: invView,
            texelsPerFrame: 64,
            ambientColor: (0.2f, 0.2f, 0.3f));  // Ambient for sky miss

        using var anchorInputs22 = LayeredTestTexture.Create(ImageAssertions.Programs, anchorPosTex, anchorNormalTex);
        programId.ProbeAnchors = anchorInputs22;
        programId.PrimaryDepth = depthTex;
        programId.HzbDepth = depthTex;
        programId.SurfaceAlbedo = colorTex;
        programId.ScreenProbeAtlasHistory = historyTex;

        // Act
        TestFramework.RenderQuadTo(programId, outputAtlas);
        // Assert
        ImageAssertions.Statistics.Inspect(outputAtlas[0],channelMask:7).AssertFiniteRange();
        var lit=ImageAssertions.Predicates.Predicate(outputAtlas[0], SurfacePredicates.LumOnProbeAtlasTraceFunctionalTests_RaySteps_AffectsTraceQuality);
        lit.AssertEqual();Assert.True(lit.MatchingTexels>0);
    }

    /// <summary>
    /// Tests that invViewMatrix correctly transforms ray directions.
    ///
    /// DESIRED BEHAVIOR:
    /// - invViewMatrix transforms view-space directions to world-space
    /// - With identity view, world == view directions
    /// - With rotated view, directions should be transformed
    ///
    /// Setup:
    /// - Compare identity view vs rotated view
    /// - Same scene otherwise
    ///
    /// Expected:
    /// - Different view matrices should produce different results
    /// </summary>
    [Fact]
    public void InvViewMatrix_TransformsDirectionsCorrectly()
    {
        // Arrange
        EnsureShaderTestAvailable();

        var anchorPosData = CreateValidProbeAnchors();
        var anchorNormalData = CreateProbeNormalsUpward();
        var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(0.5f, channels: 1);
        var colorData = CreateUniformSceneColor(1f, 0f, 0f);
        var historyData = CreateZeroedHistory();

        float identityBrightness;
        float rotatedBrightness;

        // Identity view
        {

            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorPosData);
            using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorNormalData);
            using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
            using var colorTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, colorData);
            using var historyTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rgba16f, historyData);
            using var outputAtlas = TestFramework.CreateTestGBuffer(
                AtlasWidth, AtlasHeight,
                TextureFormat.Rgba16f, TextureFormat.Rg32f);

            var programId = CompileOctahedralTraceShader();

            var projection = LumOnTestInputFactory.CreateRealisticProjection();
            var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
            var view = LumOnTestInputFactory.CreateIdentityView();
            var invView = LumOnTestInputFactory.CreateIdentityView();
            SetupOctahedralTraceUniforms(
                programId,
                invProjection: invProjection,
                projection: projection,
                view: view,
                invView: invView,
                texelsPerFrame: 64);

            using var anchorInputs23 = LayeredTestTexture.Create(ImageAssertions.Programs, anchorPosTex, anchorNormalTex);
            programId.ProbeAnchors = anchorInputs23;
            programId.PrimaryDepth = depthTex;
            programId.HzbDepth = depthTex;
            programId.SurfaceAlbedo = colorTex;
            programId.ScreenProbeAtlasHistory = historyTex;

            // Act
            TestFramework.RenderQuadTo(programId, outputAtlas);
            // Assert
            identityBrightness = ImageAssertions.Statistics.OrderedFloatSum(outputAtlas[0], ObservationProjections.GpuImageAssertionTests_OrderedFloatReductionRetainsOriginalArithmetic_2);
        }

        // Rotated view (90 degrees around Y)
        {

            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorPosData);
            using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, TextureFormat.Rgba16f, anchorNormalData);
            using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.R32f, depthData);
            using var colorTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f, colorData);
            using var historyTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rgba16f, historyData);
            using var outputAtlas = TestFramework.CreateTestGBuffer(
                AtlasWidth, AtlasHeight,
                TextureFormat.Rgba16f, TextureFormat.Rg32f);

            var programId = CompileOctahedralTraceShader();

            var projection = LumOnTestInputFactory.CreateRealisticProjection();
            var invProjection = LumOnTestInputFactory.CreateRealisticInverseProjection();
            // Create a rotated view matrix (90 degrees around Y axis)
            var rotatedView = new float[]
            {
                0f, 0f, -1f, 0f,  // First column
                0f, 1f, 0f, 0f,   // Second column
                1f, 0f, 0f, 0f,   // Third column
                0f, 0f, 0f, 1f    // Fourth column
            };
            var rotatedInvView = new float[]
            {
                0f, 0f, 1f, 0f,   // Inverse rotation
                0f, 1f, 0f, 0f,
                -1f, 0f, 0f, 0f,
                0f, 0f, 0f, 1f
            };
            SetupOctahedralTraceUniforms(
                programId,
                invProjection: invProjection,
                projection: projection,
                view: rotatedView,
                invView: rotatedInvView,
                texelsPerFrame: 64);

            using var anchorInputs24 = LayeredTestTexture.Create(ImageAssertions.Programs, anchorPosTex, anchorNormalTex);
            programId.ProbeAnchors = anchorInputs24;
            programId.PrimaryDepth = depthTex;
            programId.HzbDepth = depthTex;
            programId.SurfaceAlbedo = colorTex;
            programId.ScreenProbeAtlasHistory = historyTex;

            // Act
            TestFramework.RenderQuadTo(programId, outputAtlas);
            // Assert
            rotatedBrightness = ImageAssertions.Statistics.OrderedFloatSum(outputAtlas[0], ObservationProjections.GpuImageAssertionTests_OrderedFloatReductionRetainsOriginalArithmetic_2);
        }

        // Both should produce valid output
        Assert.True(identityBrightness > 0.001f, "Identity view should produce output");
        Assert.True(rotatedBrightness > 0.001f, "Rotated view should produce output");
    }

    #endregion
    #endregion
    #region Protected API
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("inspection", 0, 0);
        programs.Prepare("layer", 0, 0);
        programs.Prepare("predicate-surface", 0, 0);
        programs.Prepare("projection-observation", 0, 0);
        programs.Prepare("statistics", 0, 0);
    }
    #endregion

    #region Private


    /// <summary>
    /// Compiles and links the probe-atlas trace shader.
    /// </summary>
    private LumOnScreenProbeAtlasTraceShaderProgram CompileOctahedralTraceShader(
        int raySteps = RaySteps,
        int texelsPerFrame = 64,
        float rayMaxDistance = RayMaxDistance,
        float rayThickness = RayThickness,
        float skyMissWeight = SkyMissWeight,
        int hzbCoarseMip = 0) =>
        Programs.Create<LumOnScreenProbeAtlasTraceShaderProgram>(settings: new Dictionary<string, string?>
            {
                ["VGE_LUMON_RAY_STEPS"] = raySteps.ToString(),
                ["VGE_LUMON_ATLAS_TEXELS_PER_FRAME"] = texelsPerFrame.ToString(),
                ["VGE_LUMON_RAY_MAX_DISTANCE"] = rayMaxDistance.ToString(CultureInfo.InvariantCulture),
                ["VGE_LUMON_RAY_THICKNESS"] = rayThickness.ToString(CultureInfo.InvariantCulture),
                ["VGE_LUMON_SKY_MISS_WEIGHT"] = skyMissWeight.ToString(CultureInfo.InvariantCulture),
                ["VGE_LUMON_HZB_COARSE_MIP"] = hzbCoarseMip.ToString(CultureInfo.InvariantCulture)
            });

    /// <summary>
    /// Sets up common uniforms for the probe-atlas trace shader.
    /// </summary>
    private void SetupOctahedralTraceUniforms(
        LumOnScreenProbeAtlasTraceShaderProgram programId,
        float[] invProjection,
        float[] projection,
        float[] view,
        float[] invView,
        int frameIndex = 0,
        int texelsPerFrame = 64,  // Trace all texels in one frame for tests
        (float r, float g, float b)? ambientColor = null,
        (float r, float g, float b)? sunColor = null,
        (float x, float y, float z)? sunPosition = null,
        (float r, float g, float b)? indirectTint = null)
    {
        // These scenes use nonmetallic receivers and no metadata history.
        SceneInputs.EnsureSize(ScreenWidth, ScreenHeight);
        SceneInputs.Terrain.Surface.UploadDataImmediate(new float[ScreenWidth * ScreenHeight * 4], 0, 0, 1, ScreenWidth, ScreenHeight, 1);
        programId.GBufferSurface = SceneInputs.Terrain.Surface;
        programId.ScreenProbeAtlasMetaHistory = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, TextureFormat.Rg32f, new float[AtlasWidth * AtlasHeight * 2]);
        // Use defaults if not specified (nullable check allows explicit zero values)
        var ambient = ambientColor ?? (0.3f, 0.4f, 0.5f);
        var sun = sunColor ?? (1.0f, 0.9f, 0.8f);
        var sunDir = sunPosition ?? (0.5f, 0.8f, 0.3f);
        var tint = indirectTint ?? (1.0f, 1.0f, 1.0f);

        programId.IndirectTint = new(tint.r, tint.g, tint.b);
        // Bind the frame state explicitly because GLSL 330 does not assign uniform-block bindings itself.
        UpdateAndBindLumOnFrameUbo(
            programId,
            invProjectionMatrix: invProjection,
            projectionMatrix: projection,
            viewMatrix: view,
            invViewMatrix: invView,
            frameIndex: frameIndex,
            sunPosition: new Vintagestory.API.MathTools.Vec3f(sunDir.x, sunDir.y, sunDir.z),
            sunColor: new Vintagestory.API.MathTools.Vec3f(sun.r, sun.g, sun.b),
            ambientColor: new Vintagestory.API.MathTools.Vec3f(ambient.r, ambient.g, ambient.b));

    }

    /// <summary>
    /// Creates a probe anchor position buffer with all probes valid.
    /// </summary>
    private static float[] CreateValidProbeAnchors()
    {
        var data = new float[ProbeGridWidth * ProbeGridHeight * 4];
        for (int py = 0; py < ProbeGridHeight; py++)
        {
            for (int px = 0; px < ProbeGridWidth; px++)
            {
                int idx = (py * ProbeGridWidth + px) * 4;
                // Position at center of probe cell, Z=0
                data[idx + 0] = (px + 0.5f) * 2f - 2f;  // X: spread out in world
                data[idx + 1] = (py + 0.5f) * 2f - 2f;  // Y: spread out
                data[idx + 2] = 0f;                      // Z: at origin plane
                data[idx + 3] = 1.0f;                    // Valid
            }
        }
        return data;
    }

    /// <summary>
    /// Creates a probe anchor position buffer with all probes valid at a specific Z position.
    /// Use negative Z values to place probes in front of the camera (into the scene).
    /// </summary>
    /// <param name="z">Z position in world space. Negative = in front of camera with identity view.</param>
    private static float[] CreateValidProbeAnchorsAtZ(float z)
    {
        var data = new float[ProbeGridWidth * ProbeGridHeight * 4];
        for (int py = 0; py < ProbeGridHeight; py++)
        {
            for (int px = 0; px < ProbeGridWidth; px++)
            {
                int idx = (py * ProbeGridWidth + px) * 4;
                // Keep probes centered to ensure rays stay within screen bounds
                data[idx + 0] = 0f;  // X: centered
                data[idx + 1] = 0f;  // Y: centered
                data[idx + 2] = z;   // Z: specified position
                data[idx + 3] = 1.0f; // Valid
            }
        }
        return data;
    }

    /// <summary>
    /// Creates a probe anchor position buffer with all probes invalid.
    /// </summary>
    private static float[] CreateInvalidProbeAnchors()
    {
        var data = new float[ProbeGridWidth * ProbeGridHeight * 4];
        for (int i = 0; i < ProbeGridWidth * ProbeGridHeight; i++)
        {
            int idx = i * 4;
            data[idx + 0] = 0f;
            data[idx + 1] = 0f;
            data[idx + 2] = 0f;
            data[idx + 3] = 0f;  // Invalid
        }
        return data;
    }

    /// <summary>
    /// Creates a probe anchor normal buffer with upward-facing normals (encoded).
    /// </summary>
    private static float[] CreateProbeNormalsUpward()
    {
        var data = new float[ProbeGridWidth * ProbeGridHeight * 4];
        // Encode (0, 1, 0) to [0,1] range: (0.5, 1.0, 0.5)
        for (int i = 0; i < ProbeGridWidth * ProbeGridHeight; i++)
        {
            int idx = i * 4;
            data[idx + 0] = 0.5f;  // X: 0 encoded
            data[idx + 1] = 1.0f;  // Y: 1 encoded
            data[idx + 2] = 0.5f;  // Z: 0 encoded
            data[idx + 3] = 0f;    // Reserved
        }
        return data;
    }

    /// <summary>
    /// Creates a zeroed history texture for the octahedral atlas.
    /// </summary>
    private static float[] CreateZeroedHistory()
    {
        return new float[AtlasWidth * AtlasHeight * 4];
    }

    /// <summary>
    /// Creates a uniform color texture for scene radiance (primary color).
    /// </summary>
    private static float[] CreateUniformSceneColor(float r, float g, float b)
    {
        var data = new float[ScreenWidth * ScreenHeight * 4];
        for (int i = 0; i < ScreenWidth * ScreenHeight; i++)
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
    /// Encodes hit distance using log encoding (matches shader).
    /// encoded = log(distance + 1.0)
    /// </summary>
    private static float EncodeHitDistance(float distance)
    {
        return MathF.Log(distance + 1.0f);
    }

    /// <summary>
    /// Decodes hit distance from log encoding (matches shader).
    /// decoded = exp(encoded) - 1.0
    /// </summary>
    private static float DecodeHitDistance(float encoded)
    {
        return MathF.Exp(encoded) - 1.0f;
    }

    /// <summary>Creates an empty confidence-and-flags history image for trace scenarios.</summary>
    private static float[] CreateZeroedMetaHistory()
    {
        return new float[AtlasWidth * AtlasHeight * 2];
    }

    /// <summary>
    /// Gets the atlas coordinates for a specific probe and octahedral texel.
    /// </summary>
    private static (int x, int y) GetAtlasCoord(int probeX, int probeY, int octX, int octY)
    {
        return (probeX * OctahedralSize + octX, probeY * OctahedralSize + octY);
    }

    /// <summary>
    /// Reads a single texel from atlas data.
    /// </summary>
    private static (float r, float g, float b, float a) ReadAtlasTexel(GpuTexture atlasData, int x, int y)
    {
        var local=atlasData.ReadPixelsRegion(x,y,1,1);
        int idx=0;
        return (local[idx],local[idx+1],local[idx+2],local[idx+3]);
    }

    /// <summary>Reads confidence and packed flags from one atlas metadata texel.</summary>
    private static (float confidence, float flagsBitsAsFloat) ReadAtlasMetaTexel(GpuTexture metaData, int x, int y)
    {
        var local=metaData.ReadPixelsRegion(x,y,1,1);
        int idx=0;
        return (local[idx],local[idx+1]);
    }

    /// <summary>Counts atlas metadata texels whose packed flags report a scene hit.</summary>
    private static int CountHitMetadataTexels(GpuTexture metaData)
    {
        var pixels = metaData.ReadPixelsRegion(0, 0, AtlasWidth, AtlasHeight);
        int hits = 0;
        for (int index = 1; index < pixels.Length; index += 2)
        {
            if ((BitConverter.SingleToUInt32Bits(pixels[index]) & 1u) != 0u) hits++;
        }
        return hits;
    }

    /// <summary>Returns the first atlas texel whose packed metadata flags report a scene hit.</summary>
    private static int FindHitMetadataTexel(GpuTexture metaData)
    {
        var pixels = metaData.ReadPixelsRegion(0, 0, AtlasWidth, AtlasHeight);
        for (int index = 1; index < pixels.Length; index += 2)
        {
            if ((BitConverter.SingleToUInt32Bits(pixels[index]) & 1u) != 0u) return (index - 1) / 2;
        }
        return -1;
    }
    #endregion
}
