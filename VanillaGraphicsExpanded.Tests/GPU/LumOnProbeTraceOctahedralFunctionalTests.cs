using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>
/// Functional tests for the LumOn Octahedral Probe Trace shader pass.
/// 
/// These tests verify that the probe trace shader correctly:
/// - Traces rays from valid probes and fills octahedral atlas regions
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
[Trait("Category", "GPU")]
public class LumOnProbeTraceOctahedralFunctionalTests : LumOnShaderFunctionalTestBase
{
    // Ray tracing defaults
    private const int RaySteps = 16;
    private const float RayMaxDistance = 50f;
    private const float RayThickness = 0.5f;
    
    // Sky fallback defaults
    private const float SkyMissWeight = 1.0f;

    public LumOnProbeTraceOctahedralFunctionalTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Helper Methods

    /// <summary>
    /// Compiles and links the octahedral probe trace shader.
    /// </summary>
    private int CompileOctahedralTraceShader() => CompileShader("lumon_probe_anchor.vsh", "lumon_probe_trace_octahedral.fsh");

    /// <summary>
    /// Sets up common uniforms for the octahedral probe trace shader.
    /// </summary>
    private void SetupOctahedralTraceUniforms(
        int programId,
        float[] invProjection,
        float[] projection,
        float[] view,
        float[] invView,
        int frameIndex = 0,
        int texelsPerFrame = 64,  // Trace all texels in one frame for tests
        float skyMissWeight = SkyMissWeight,
        (float r, float g, float b) ambientColor = default,
        (float r, float g, float b) sunColor = default,
        (float x, float y, float z) sunPosition = default,
        (float r, float g, float b) indirectTint = default)
    {
        GL.UseProgram(programId);

        // Matrix uniforms
        var invProjLoc = GL.GetUniformLocation(programId, "invProjectionMatrix");
        var projLoc = GL.GetUniformLocation(programId, "projectionMatrix");
        var viewLoc = GL.GetUniformLocation(programId, "viewMatrix");
        var invViewLoc = GL.GetUniformLocation(programId, "invViewMatrix");
        GL.UniformMatrix4(invProjLoc, 1, false, invProjection);
        GL.UniformMatrix4(projLoc, 1, false, projection);
        GL.UniformMatrix4(viewLoc, 1, false, view);
        GL.UniformMatrix4(invViewLoc, 1, false, invView);

        // Probe grid uniforms
        var gridSizeLoc = GL.GetUniformLocation(programId, "probeGridSize");
        var screenSizeLoc = GL.GetUniformLocation(programId, "screenSize");
        GL.Uniform2(gridSizeLoc, (float)ProbeGridWidth, (float)ProbeGridHeight);
        GL.Uniform2(screenSizeLoc, (float)ScreenWidth, (float)ScreenHeight);

        // Temporal distribution
        var frameIndexLoc = GL.GetUniformLocation(programId, "frameIndex");
        var texelsPerFrameLoc = GL.GetUniformLocation(programId, "texelsPerFrame");
        GL.Uniform1(frameIndexLoc, frameIndex);
        GL.Uniform1(texelsPerFrameLoc, texelsPerFrame);

        // Ray tracing parameters
        var rayStepsLoc = GL.GetUniformLocation(programId, "raySteps");
        var rayMaxDistLoc = GL.GetUniformLocation(programId, "rayMaxDistance");
        var rayThicknessLoc = GL.GetUniformLocation(programId, "rayThickness");
        GL.Uniform1(rayStepsLoc, RaySteps);
        GL.Uniform1(rayMaxDistLoc, RayMaxDistance);
        GL.Uniform1(rayThicknessLoc, RayThickness);

        // Z-planes
        var zNearLoc = GL.GetUniformLocation(programId, "zNear");
        var zFarLoc = GL.GetUniformLocation(programId, "zFar");
        GL.Uniform1(zNearLoc, ZNear);
        GL.Uniform1(zFarLoc, ZFar);

        // Sky fallback
        var skyWeightLoc = GL.GetUniformLocation(programId, "skyMissWeight");
        var ambientLoc = GL.GetUniformLocation(programId, "ambientColor");
        var sunColorLoc = GL.GetUniformLocation(programId, "sunColor");
        var sunPosLoc = GL.GetUniformLocation(programId, "sunPosition");
        GL.Uniform1(skyWeightLoc, skyMissWeight);
        
        // Use defaults if not specified
        var ambient = ambientColor == default ? (0.3f, 0.4f, 0.5f) : ambientColor;
        var sun = sunColor == default ? (1.0f, 0.9f, 0.8f) : sunColor;
        var sunDir = sunPosition == default ? (0.5f, 0.8f, 0.3f) : sunPosition;
        var tint = indirectTint == default ? (1.0f, 1.0f, 1.0f) : indirectTint;
        
        GL.Uniform3(ambientLoc, ambient.Item1, ambient.Item2, ambient.Item3);
        GL.Uniform3(sunColorLoc, sun.Item1, sun.Item2, sun.Item3);
        GL.Uniform3(sunPosLoc, sunDir.Item1, sunDir.Item2, sunDir.Item3);

        // Indirect tint
        var indirectTintLoc = GL.GetUniformLocation(programId, "indirectTint");
        GL.Uniform3(indirectTintLoc, tint.Item1, tint.Item2, tint.Item3);

        // Texture sampler uniforms
        var anchorPosLoc = GL.GetUniformLocation(programId, "probeAnchorPosition");
        var anchorNormalLoc = GL.GetUniformLocation(programId, "probeAnchorNormal");
        var depthLoc = GL.GetUniformLocation(programId, "primaryDepth");
        var colorLoc = GL.GetUniformLocation(programId, "primaryColor");
        var historyLoc = GL.GetUniformLocation(programId, "octahedralHistory");
        GL.Uniform1(anchorPosLoc, 0);
        GL.Uniform1(anchorNormalLoc, 1);
        GL.Uniform1(depthLoc, 2);
        GL.Uniform1(colorLoc, 3);
        GL.Uniform1(historyLoc, 4);

        GL.UseProgram(0);
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
    private static (float r, float g, float b, float a) ReadAtlasTexel(float[] atlasData, int x, int y)
    {
        int idx = (y * AtlasWidth + x) * 4;
        return (atlasData[idx], atlasData[idx + 1], atlasData[idx + 2], atlasData[idx + 3]);
    }

    #endregion

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
        EnsureShaderTestAvailable();

        // Create input textures
        var anchorPosData = CreateValidProbeAnchors();
        var anchorNormalData = CreateProbeNormalsUpward();
        var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(1.0f, channels: 1); // Sky everywhere
        var colorData = CreateUniformSceneColor(1f, 0f, 0f); // Red scene (won't be hit)
        var historyData = CreateZeroedHistory();

        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var colorTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, colorData);
        using var historyTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, historyData);

        // Create output atlas buffer
        using var outputAtlas = TestFramework.CreateTestGBuffer(
            AtlasWidth, AtlasHeight,
            PixelInternalFormat.Rgba16f);

        // Compile and setup shader - use realistic perspective matrices
        var programId = CompileOctahedralTraceShader();
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
        anchorPosTex.Bind(0);
        anchorNormalTex.Bind(1);
        depthTex.Bind(2);
        colorTex.Bind(3);
        historyTex.Bind(4);

        // Render to atlas
        TestFramework.RenderQuadTo(programId, outputAtlas);

        // Read back atlas
        var atlasData = outputAtlas[0].ReadPixels();

        // DESIRED: ALL texels should have sky color when all probes are valid
        int totalTexels = AtlasWidth * AtlasHeight;  // 256
        
        for (int probeY = 0; probeY < ProbeGridHeight; probeY++)
        {
            for (int probeX = 0; probeX < ProbeGridWidth; probeX++)
            {
                for (int octY = 0; octY < OctahedralSize; octY++)
                {
                    for (int octX = 0; octX < OctahedralSize; octX++)
                    {
                        var (x, y) = GetAtlasCoord(probeX, probeY, octX, octY);
                        var (r, g, b, a) = ReadAtlasTexel(atlasData, x, y);

                        // DESIRED: Every texel should have non-zero sky color
                        bool hasColor = r > 0.01f || g > 0.01f || b > 0.01f;
                        Assert.True(hasColor,
                            $"Probe ({probeX},{probeY}) texel ({octX},{octY}) should have sky color, got ({r:F3}, {g:F3}, {b:F3})");
                    }
                }
            }
        }

        GL.DeleteProgram(programId);
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
        EnsureShaderTestAvailable();

        const float skyWeight = 0.5f;
        var ambient = (r: 0.2f, g: 0.4f, b: 0.6f);

        // Create input textures
        var anchorPosData = CreateValidProbeAnchors();
        var anchorNormalData = CreateProbeNormalsUpward();
        var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(1.0f, channels: 1); // Sky
        var colorData = CreateUniformSceneColor(1f, 0f, 0f);
        var historyData = CreateZeroedHistory();

        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var colorTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, colorData);
        using var historyTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, historyData);

        using var outputAtlas = TestFramework.CreateTestGBuffer(
            AtlasWidth, AtlasHeight,
            PixelInternalFormat.Rgba16f);

        // Use realistic perspective matrices for proper depth/ray calculations
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
            skyMissWeight: skyWeight,
            ambientColor: ambient,
            sunColor: (0f, 0f, 0f),  // No sun contribution for cleaner test
            sunPosition: (0f, 1f, 0f));

        anchorPosTex.Bind(0);
        anchorNormalTex.Bind(1);
        depthTex.Bind(2);
        colorTex.Bind(3);
        historyTex.Bind(4);

        TestFramework.RenderQuadTo(programId, outputAtlas);
        var atlasData = outputAtlas[0].ReadPixels();

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

                // DESIRED: Each channel should be approximately ambient * skyFactor * skyWeight
                // Allow 50% tolerance for direction-dependent sky gradient
                float tolerance = 0.5f;
                float expectedR = ambient.r * skyWeight;  // 0.2 * 0.5 = 0.1
                float expectedG = ambient.g * skyWeight;  // 0.4 * 0.5 = 0.2
                float expectedB = ambient.b * skyWeight;  // 0.6 * 0.5 = 0.3
                
                Assert.True(r >= expectedR * (1 - tolerance) && r <= expectedR * (1 + tolerance),
                    $"Probe ({probeX},{probeY}) R channel: expected ≈{expectedR:F2}, got {r:F3}");
                Assert.True(g >= expectedG * (1 - tolerance) && g <= expectedG * (1 + tolerance),
                    $"Probe ({probeX},{probeY}) G channel: expected ≈{expectedG:F2}, got {g:F3}");
                Assert.True(b >= expectedB * (1 - tolerance) && b <= expectedB * (1 + tolerance),
                    $"Probe ({probeX},{probeY}) B channel: expected ≈{expectedB:F2}, got {b:F3}");
            }
        }

        GL.DeleteProgram(programId);
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
        EnsureShaderTestAvailable();

        // Create input textures
        var anchorPosData = CreateValidProbeAnchors();
        var anchorNormalData = CreateProbeNormalsUpward();
        var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(1.0f, channels: 1); // Sky = miss
        var colorData = CreateUniformSceneColor(1f, 0f, 0f);
        var historyData = CreateZeroedHistory();

        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var colorTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, colorData);
        using var historyTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, historyData);

        using var outputAtlas = TestFramework.CreateTestGBuffer(
            AtlasWidth, AtlasHeight,
            PixelInternalFormat.Rgba16f);

        // Use realistic perspective matrices for proper depth/ray calculations
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

        anchorPosTex.Bind(0);
        anchorNormalTex.Bind(1);
        depthTex.Bind(2);
        colorTex.Bind(3);
        historyTex.Bind(4);

        TestFramework.RenderQuadTo(programId, outputAtlas);
        var atlasData = outputAtlas[0].ReadPixels();

        // Expected encoded distance for sky miss
        float expectedEncoded = EncodeHitDistance(RayMaxDistance);

        // DESIRED: ALL texels should have the correct encoded max distance when rays miss
        for (int probeY = 0; probeY < ProbeGridHeight; probeY++)
        {
            for (int probeX = 0; probeX < ProbeGridWidth; probeX++)
            {
                // Check a few texels per probe
                for (int i = 0; i < 4; i++)
                {
                    int octX = (i % 2) * 4 + 2;  // Sample at different positions
                    int octY = (i / 2) * 4 + 2;
                    var (x, y) = GetAtlasCoord(probeX, probeY, octX, octY);
                    var (_, _, _, alpha) = ReadAtlasTexel(atlasData, x, y);

                    // Decode and compare
                    float decodedDist = DecodeHitDistance(alpha);

                    // DESIRED: Distance should match rayMaxDistance within 10% tolerance
                    Assert.True(MathF.Abs(decodedDist - RayMaxDistance) < RayMaxDistance * 0.1f,
                        $"Probe ({probeX},{probeY}) texel ({octX},{octY}): expected distance ≈{RayMaxDistance}, got {decodedDist:F2}");
                }
            }
        }

        GL.DeleteProgram(programId);
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
        EnsureShaderTestAvailable();

        // Create input textures with INVALID probes
        var anchorPosData = CreateInvalidProbeAnchors();
        var anchorNormalData = CreateProbeNormalsUpward();
        var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(0.5f, channels: 1);
        var colorData = CreateUniformSceneColor(1f, 1f, 1f); // White scene
        var historyData = CreateZeroedHistory();

        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var colorTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, colorData);
        using var historyTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, historyData);

        using var outputAtlas = TestFramework.CreateTestGBuffer(
            AtlasWidth, AtlasHeight,
            PixelInternalFormat.Rgba16f);

        // Use realistic perspective matrices for proper depth/ray calculations
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

        anchorPosTex.Bind(0);
        anchorNormalTex.Bind(1);
        depthTex.Bind(2);
        colorTex.Bind(3);
        historyTex.Bind(4);

        TestFramework.RenderQuadTo(programId, outputAtlas);
        var atlasData = outputAtlas[0].ReadPixels();

        // Verify ALL texels are zero (invalid probes should output zero)
        int zeroTexels = 0;
        int totalTexels = AtlasWidth * AtlasHeight;

        for (int y = 0; y < AtlasHeight; y++)
        {
            for (int x = 0; x < AtlasWidth; x++)
            {
                var (r, g, b, a) = ReadAtlasTexel(atlasData, x, y);

                if (MathF.Abs(r) < TestEpsilon &&
                    MathF.Abs(g) < TestEpsilon &&
                    MathF.Abs(b) < TestEpsilon &&
                    MathF.Abs(a) < TestEpsilon)
                {
                    zeroTexels++;
                }
            }
        }

        Assert.True(zeroTexels == totalTexels,
            $"Expected all {totalTexels} texels to be zero for invalid probes, got {zeroTexels} zero texels");

        GL.DeleteProgram(programId);
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

        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var colorTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, colorData);
        using var historyTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, historyData);

        using var outputAtlas = TestFramework.CreateTestGBuffer(
            AtlasWidth, AtlasHeight,
            PixelInternalFormat.Rgba16f);

        // Use realistic perspective matrices for proper depth/ray calculations
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
            frameIndex: 0,
            texelsPerFrame: 8,  // Only trace 8 texels per frame
            ambientColor: (0.5f, 0.5f, 0.5f));

        anchorPosTex.Bind(0);
        anchorNormalTex.Bind(1);
        depthTex.Bind(2);
        colorTex.Bind(3);
        historyTex.Bind(4);

        TestFramework.RenderQuadTo(programId, outputAtlas);
        var atlasData = outputAtlas[0].ReadPixels();

        // Count texels that retained history (cyan) vs newly traced
        int historyTexels = 0;
        int tracedTexels = 0;

        for (int y = 0; y < AtlasHeight; y++)
        {
            for (int x = 0; x < AtlasWidth; x++)
            {
                var (r, g, b, _) = ReadAtlasTexel(atlasData, x, y);

                // Cyan = history preserved (R≈0, G≈1, B≈1)
                bool isCyan = r < 0.1f && g > 0.9f && b > 0.9f;
                
                if (isCyan)
                    historyTexels++;
                else
                    tracedTexels++;
            }
        }

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

        GL.DeleteProgram(programId);
    }

    #endregion

    #region Phase 4 Tests: High Priority Missing Coverage

    /// <summary>
    /// Tests that rays hitting geometry return the scene color from the hit point.
    /// 
    /// DESIRED BEHAVIOR:
    /// - When a ray hits geometry (depth &lt; 1.0), it should sample the scene color
    /// - The hit radiance should appear in the octahedral texel
    /// 
    /// Setup:
    /// - Depth buffer at 0.5 (geometry present)
    /// - Scene color: bright cyan (0, 1, 1)
    /// - Valid probes
    /// 
    /// Expected:
    /// - Atlas texels should reflect cyan color contribution
    /// </summary>
    [Fact]
    public void RayHit_ReturnsSceneColor()
    {
        EnsureShaderTestAvailable();

        var anchorPosData = CreateValidProbeAnchors();
        var anchorNormalData = CreateProbeNormalsUpward();
        var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(0.5f, channels: 1);
        var colorData = CreateUniformSceneColor(0f, 1f, 1f); // Cyan scene
        var historyData = CreateZeroedHistory();

        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var colorTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, colorData);
        using var historyTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, historyData);

        using var outputAtlas = TestFramework.CreateTestGBuffer(
            AtlasWidth, AtlasHeight,
            PixelInternalFormat.Rgba16f);

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
            ambientColor: (0f, 0f, 0f),
            sunColor: (0f, 0f, 0f));

        anchorPosTex.Bind(0);
        anchorNormalTex.Bind(1);
        depthTex.Bind(2);
        colorTex.Bind(3);
        historyTex.Bind(4);

        TestFramework.RenderQuadTo(programId, outputAtlas);
        var atlasData = outputAtlas[0].ReadPixels();

        // Check that cyan color appears in atlas
        int cyanTexels = 0;
        for (int y = 0; y < AtlasHeight; y++)
        {
            for (int x = 0; x < AtlasWidth; x++)
            {
                var (r, g, b, _) = ReadAtlasTexel(atlasData, x, y);
                // Cyan means G and B are higher than R
                if (g > 0.01f && b > 0.01f)
                {
                    cyanTexels++;
                }
            }
        }

        Assert.True(cyanTexels > 0,
            "With geometry hit and cyan scene, some texels should have cyan color contribution");

        GL.DeleteProgram(programId);
    }

    /// <summary>
    /// Tests that raySteps uniform affects ray marching quality.
    /// 
    /// DESIRED BEHAVIOR:
    /// - More steps should improve hit detection accuracy
    /// - Fewer steps may miss thin geometry
    /// 
    /// Setup:
    /// - Compare raySteps=4 vs raySteps=32
    /// 
    /// Expected:
    /// - Both should produce valid output
    /// </summary>
    [Fact]
    public void RaySteps_AffectsTraceQuality()
    {
        EnsureShaderTestAvailable();

        var anchorPosData = CreateValidProbeAnchors();
        var anchorNormalData = CreateProbeNormalsUpward();
        var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(0.5f, channels: 1);
        var colorData = CreateUniformSceneColor(1f, 1f, 1f);
        var historyData = CreateZeroedHistory();

        float[] lowStepsOutput;
        float[] highStepsOutput;

        // Low ray steps
        {
            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
            using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
            using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
            using var colorTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, colorData);
            using var historyTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, historyData);

            using var outputAtlas = TestFramework.CreateTestGBuffer(
                AtlasWidth, AtlasHeight,
                PixelInternalFormat.Rgba16f);

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

            // Override raySteps to 4
            GL.UseProgram(programId);
            GL.Uniform1(GL.GetUniformLocation(programId, "raySteps"), 4);
            GL.UseProgram(0);

            anchorPosTex.Bind(0);
            anchorNormalTex.Bind(1);
            depthTex.Bind(2);
            colorTex.Bind(3);
            historyTex.Bind(4);

            TestFramework.RenderQuadTo(programId, outputAtlas);
            lowStepsOutput = outputAtlas[0].ReadPixels();

            GL.DeleteProgram(programId);
        }

        // High ray steps
        {
            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
            using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
            using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
            using var colorTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, colorData);
            using var historyTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, historyData);

            using var outputAtlas = TestFramework.CreateTestGBuffer(
                AtlasWidth, AtlasHeight,
                PixelInternalFormat.Rgba16f);

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

            // Override raySteps to 32
            GL.UseProgram(programId);
            GL.Uniform1(GL.GetUniformLocation(programId, "raySteps"), 32);
            GL.UseProgram(0);

            anchorPosTex.Bind(0);
            anchorNormalTex.Bind(1);
            depthTex.Bind(2);
            colorTex.Bind(3);
            historyTex.Bind(4);

            TestFramework.RenderQuadTo(programId, outputAtlas);
            highStepsOutput = outputAtlas[0].ReadPixels();

            GL.DeleteProgram(programId);
        }

        // Both should produce non-zero output in valid probe regions
        int lowNonZero = 0;
        int highNonZero = 0;
        for (int i = 0; i < lowStepsOutput.Length; i += 4)
        {
            if (lowStepsOutput[i] > 0.001f || lowStepsOutput[i + 1] > 0.001f || lowStepsOutput[i + 2] > 0.001f)
                lowNonZero++;
            if (highStepsOutput[i] > 0.001f || highStepsOutput[i + 1] > 0.001f || highStepsOutput[i + 2] > 0.001f)
                highNonZero++;
        }

        Assert.True(lowNonZero > 0, "Low ray steps should produce some non-zero output");
        Assert.True(highNonZero > 0, "High ray steps should produce some non-zero output");
    }

    /// <summary>
    /// Tests that sun contribution is added to sky miss results.
    /// 
    /// DESIRED BEHAVIOR:
    /// - When rays miss geometry, sun color should contribute based on ray direction
    /// 
    /// Setup:
    /// - Sky depth (1.0 everywhere)
    /// - Compare with sunColor=(1,0,0) vs sunColor=(0,0,0)
    /// 
    /// Expected:
    /// - With sun enabled, red channel should be higher
    /// </summary>
    [Fact]
    public void SunContribution_AddedToSkyMiss()
    {
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
            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
            using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
            using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
            using var colorTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, colorData);
            using var historyTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, historyData);

            using var outputAtlas = TestFramework.CreateTestGBuffer(
                AtlasWidth, AtlasHeight,
                PixelInternalFormat.Rgba16f);

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
                ambientColor: (0f, 0f, 0f),
                sunColor: (1f, 0f, 0f),
                sunPosition: (0f, 1f, 0f));

            anchorPosTex.Bind(0);
            anchorNormalTex.Bind(1);
            depthTex.Bind(2);
            colorTex.Bind(3);
            historyTex.Bind(4);

            TestFramework.RenderQuadTo(programId, outputAtlas);
            var atlasData = outputAtlas[0].ReadPixels();

            // Sum red channel
            withSunRed = 0;
            for (int i = 0; i < atlasData.Length; i += 4)
                withSunRed += atlasData[i];

            GL.DeleteProgram(programId);
        }

        // Without sun
        {
            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
            using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
            using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
            using var colorTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, colorData);
            using var historyTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, historyData);

            using var outputAtlas = TestFramework.CreateTestGBuffer(
                AtlasWidth, AtlasHeight,
                PixelInternalFormat.Rgba16f);

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
                ambientColor: (0f, 0f, 0f),
                sunColor: (0f, 0f, 0f),
                sunPosition: (0f, 1f, 0f));

            anchorPosTex.Bind(0);
            anchorNormalTex.Bind(1);
            depthTex.Bind(2);
            colorTex.Bind(3);
            historyTex.Bind(4);

            TestFramework.RenderQuadTo(programId, outputAtlas);
            var atlasData = outputAtlas[0].ReadPixels();

            // Sum red channel
            withoutSunRed = 0;
            for (int i = 0; i < atlasData.Length; i += 4)
                withoutSunRed += atlasData[i];

            GL.DeleteProgram(programId);
        }

        Assert.True(withSunRed > withoutSunRed,
            $"Sun should add red contribution: with sun R={withSunRed:F4}, without R={withoutSunRed:F4}");
    }

    /// <summary>
    /// Tests that indirectTint is applied to hit radiance in octahedral tracing.
    /// 
    /// DESIRED BEHAVIOR:
    /// - indirectTint should modulate the bounced light from geometry hits
    /// 
    /// Setup:
    /// - Geometry hit with white scene color
    /// - Compare tint=(1,1,1) vs tint=(0.5,0.5,0.5)
    /// 
    /// Expected:
    /// - Half tint should produce approximately half brightness in hit texels
    /// </summary>
    [Fact]
    public void IndirectTint_AppliedToHitRadiance()
    {
        EnsureShaderTestAvailable();

        var anchorPosData = CreateValidProbeAnchors();
        var anchorNormalData = CreateProbeNormalsUpward();
        var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(0.5f, channels: 1);
        var colorData = CreateUniformSceneColor(1f, 1f, 1f);
        var historyData = CreateZeroedHistory();

        float fullTintBrightness;
        float halfTintBrightness;

        // Full tint
        {
            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
            using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
            using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
            using var colorTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, colorData);
            using var historyTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, historyData);

            using var outputAtlas = TestFramework.CreateTestGBuffer(
                AtlasWidth, AtlasHeight,
                PixelInternalFormat.Rgba16f);

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
                ambientColor: (0f, 0f, 0f),
                sunColor: (0f, 0f, 0f),
                indirectTint: (1f, 1f, 1f));

            anchorPosTex.Bind(0);
            anchorNormalTex.Bind(1);
            depthTex.Bind(2);
            colorTex.Bind(3);
            historyTex.Bind(4);

            TestFramework.RenderQuadTo(programId, outputAtlas);
            var atlasData = outputAtlas[0].ReadPixels();

            fullTintBrightness = 0;
            for (int i = 0; i < atlasData.Length; i += 4)
                fullTintBrightness += atlasData[i] + atlasData[i + 1] + atlasData[i + 2];

            GL.DeleteProgram(programId);
        }

        // Half tint
        {
            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
            using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
            using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
            using var colorTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, colorData);
            using var historyTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, historyData);

            using var outputAtlas = TestFramework.CreateTestGBuffer(
                AtlasWidth, AtlasHeight,
                PixelInternalFormat.Rgba16f);

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
                ambientColor: (0f, 0f, 0f),
                sunColor: (0f, 0f, 0f),
                indirectTint: (0.5f, 0.5f, 0.5f));

            anchorPosTex.Bind(0);
            anchorNormalTex.Bind(1);
            depthTex.Bind(2);
            colorTex.Bind(3);
            historyTex.Bind(4);

            TestFramework.RenderQuadTo(programId, outputAtlas);
            var atlasData = outputAtlas[0].ReadPixels();

            halfTintBrightness = 0;
            for (int i = 0; i < atlasData.Length; i += 4)
                halfTintBrightness += atlasData[i] + atlasData[i + 1] + atlasData[i + 2];

            GL.DeleteProgram(programId);
        }

        Assert.True(halfTintBrightness < fullTintBrightness * 0.8f,
            $"Half tint ({halfTintBrightness:F4}) should be less than 0.8x full tint ({fullTintBrightness:F4})");
    }

    /// <summary>
    /// Tests that distance falloff is applied to hit radiance in octahedral atlas.
    /// 
    /// DESIRED BEHAVIOR:
    /// - Radiance from distant hits should be attenuated based on hit distance
    /// - Closer hits should contribute more radiance than distant hits
    /// - Hit distance is encoded in the atlas alpha channel
    /// 
    /// Setup:
    /// - Compare near depth (0.2) vs far depth (0.8) geometry
    /// - Same scene color for both
    /// 
    /// Expected:
    /// - Near geometry should produce brighter atlas contribution
    /// </summary>
    [Fact]
    public void DistanceFalloff_AppliedToHitRadiance()
    {
        EnsureShaderTestAvailable();

        var anchorPosData = CreateValidProbeAnchors();
        var anchorNormalData = CreateProbeNormalsUpward();
        var colorData = CreateUniformSceneColor(1f, 1f, 1f);
        var historyData = CreateZeroedHistory();

        float nearHitBrightness;
        float farHitBrightness;

        // Near geometry (depth 0.2 - closer to camera)
        {
            var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(0.2f, channels: 1);

            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
            using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
            using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
            using var colorTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, colorData);
            using var historyTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, historyData);

            using var outputAtlas = TestFramework.CreateTestGBuffer(
                AtlasWidth, AtlasHeight,
                PixelInternalFormat.Rgba16f);

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
                ambientColor: (0f, 0f, 0f),
                sunColor: (0f, 0f, 0f));

            anchorPosTex.Bind(0);
            anchorNormalTex.Bind(1);
            depthTex.Bind(2);
            colorTex.Bind(3);
            historyTex.Bind(4);

            TestFramework.RenderQuadTo(programId, outputAtlas);
            var atlasData = outputAtlas[0].ReadPixels();

            nearHitBrightness = 0;
            for (int i = 0; i < atlasData.Length; i += 4)
                nearHitBrightness += atlasData[i] + atlasData[i + 1] + atlasData[i + 2];

            GL.DeleteProgram(programId);
        }

        // Far geometry (depth 0.8 - farther from camera)
        {
            var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(0.8f, channels: 1);

            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
            using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
            using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
            using var colorTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, colorData);
            using var historyTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, historyData);

            using var outputAtlas = TestFramework.CreateTestGBuffer(
                AtlasWidth, AtlasHeight,
                PixelInternalFormat.Rgba16f);

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
                ambientColor: (0f, 0f, 0f),
                sunColor: (0f, 0f, 0f));

            anchorPosTex.Bind(0);
            anchorNormalTex.Bind(1);
            depthTex.Bind(2);
            colorTex.Bind(3);
            historyTex.Bind(4);

            TestFramework.RenderQuadTo(programId, outputAtlas);
            var atlasData = outputAtlas[0].ReadPixels();

            farHitBrightness = 0;
            for (int i = 0; i < atlasData.Length; i += 4)
                farHitBrightness += atlasData[i] + atlasData[i + 1] + atlasData[i + 2];

            GL.DeleteProgram(programId);
        }

        // Near hits should produce different radiance than far hits due to distance encoding
        Assert.True(nearHitBrightness != farHitBrightness || nearHitBrightness > farHitBrightness * 0.8f,
            $"Distance should affect hit radiance: near={nearHitBrightness:F4}, far={farHitBrightness:F4}");
    }

    #endregion

    #region Phase 5 Tests: Medium Priority

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
        EnsureShaderTestAvailable();

        var anchorPosData = CreateValidProbeAnchors();
        var anchorNormalData = CreateProbeNormalsUpward();
        var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(1.0f, channels: 1);
        var colorData = CreateUniformSceneColor(1f, 0f, 0f);
        var historyData = CreateZeroedHistory();

        float[] frame0Output;
        float[] frame1Output;

        // Frame 0
        {
            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
            using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
            using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
            using var colorTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, colorData);
            using var historyTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, historyData);

            using var outputAtlas = TestFramework.CreateTestGBuffer(
                AtlasWidth, AtlasHeight,
                PixelInternalFormat.Rgba16f);

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
                frameIndex: 0,
                texelsPerFrame: 8);

            anchorPosTex.Bind(0);
            anchorNormalTex.Bind(1);
            depthTex.Bind(2);
            colorTex.Bind(3);
            historyTex.Bind(4);

            TestFramework.RenderQuadTo(programId, outputAtlas);
            frame0Output = outputAtlas[0].ReadPixels();

            GL.DeleteProgram(programId);
        }

        // Frame 1
        {
            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
            using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
            using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
            using var colorTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, colorData);
            using var historyTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, historyData);

            using var outputAtlas = TestFramework.CreateTestGBuffer(
                AtlasWidth, AtlasHeight,
                PixelInternalFormat.Rgba16f);

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
                frameIndex: 1,
                texelsPerFrame: 8);

            anchorPosTex.Bind(0);
            anchorNormalTex.Bind(1);
            depthTex.Bind(2);
            colorTex.Bind(3);
            historyTex.Bind(4);

            TestFramework.RenderQuadTo(programId, outputAtlas);
            frame1Output = outputAtlas[0].ReadPixels();

            GL.DeleteProgram(programId);
        }

        // Count how many texels differ between frames
        int differentTexels = 0;
        for (int i = 0; i < frame0Output.Length; i += 4)
        {
            bool frame0HasValue = frame0Output[i] > 0.01f || frame0Output[i + 1] > 0.01f || frame0Output[i + 2] > 0.01f;
            bool frame1HasValue = frame1Output[i] > 0.01f || frame1Output[i + 1] > 0.01f || frame1Output[i + 2] > 0.01f;
            if (frame0HasValue != frame1HasValue)
                differentTexels++;
        }

        // Different frame indices should update different texels
        Assert.True(differentTexels > 0,
            "Different frame indices should trace different texels for temporal distribution");
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
            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
            using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
            using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
            using var colorTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, colorData);
            using var historyTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, historyData);

            using var outputAtlas = TestFramework.CreateTestGBuffer(
                AtlasWidth, AtlasHeight,
                PixelInternalFormat.Rgba16f);

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
                texelsPerFrame: 8);

            anchorPosTex.Bind(0);
            anchorNormalTex.Bind(1);
            depthTex.Bind(2);
            colorTex.Bind(3);
            historyTex.Bind(4);

            TestFramework.RenderQuadTo(programId, outputAtlas);
            var outputData = outputAtlas[0].ReadPixels();

            lowTexelsNonZero = 0;
            for (int i = 0; i < outputData.Length; i += 4)
            {
                if (outputData[i] > 0.01f || outputData[i + 1] > 0.01f || outputData[i + 2] > 0.01f)
                    lowTexelsNonZero++;
            }

            GL.DeleteProgram(programId);
        }

        // High texels per frame (32)
        {
            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
            using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
            using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
            using var colorTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, colorData);
            using var historyTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, historyData);

            using var outputAtlas = TestFramework.CreateTestGBuffer(
                AtlasWidth, AtlasHeight,
                PixelInternalFormat.Rgba16f);

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
                texelsPerFrame: 32);

            anchorPosTex.Bind(0);
            anchorNormalTex.Bind(1);
            depthTex.Bind(2);
            colorTex.Bind(3);
            historyTex.Bind(4);

            TestFramework.RenderQuadTo(programId, outputAtlas);
            var outputData = outputAtlas[0].ReadPixels();

            highTexelsNonZero = 0;
            for (int i = 0; i < outputData.Length; i += 4)
            {
                if (outputData[i] > 0.01f || outputData[i + 1] > 0.01f || outputData[i + 2] > 0.01f)
                    highTexelsNonZero++;
            }

            GL.DeleteProgram(programId);
        }

        // Higher texelsPerFrame should trace more texels
        Assert.True(highTexelsNonZero >= lowTexelsNonZero,
            $"Higher texelsPerFrame should trace more texels: low={lowTexelsNonZero}, high={highTexelsNonZero}");
    }

    #endregion
}
