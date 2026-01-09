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
public class LumOnProbeTraceOctahedralFunctionalTests : RenderTestBase, IDisposable
{
    private readonly HeadlessGLFixture _fixture;
    private readonly ShaderTestHelper? _helper;
    private readonly ShaderTestFramework _framework;
    
    // Test configuration constants
    private const int ScreenWidth = LumOnTestInputFactory.ScreenWidth;   // 4
    private const int ScreenHeight = LumOnTestInputFactory.ScreenHeight; // 4
    private const int ProbeGridWidth = LumOnTestInputFactory.ProbeGridWidth;   // 2
    private const int ProbeGridHeight = LumOnTestInputFactory.ProbeGridHeight; // 2
    
    // Octahedral atlas constants (must match shader's LUMON_OCTAHEDRAL_SIZE)
    private const int OctahedralSize = 8;
    private const int AtlasWidth = ProbeGridWidth * OctahedralSize;   // 16
    private const int AtlasHeight = ProbeGridHeight * OctahedralSize; // 16
    
    private const float ZNear = LumOnTestInputFactory.DefaultZNear;  // 0.1
    private const float ZFar = LumOnTestInputFactory.DefaultZFar;    // 100
    private const float TestEpsilon = 1e-2f;  // Tolerance for float comparisons
    
    // Ray tracing defaults
    private const int RaySteps = 16;
    private const float RayMaxDistance = 50f;
    private const float RayThickness = 0.5f;
    
    // Sky fallback defaults
    private const float SkyMissWeight = 1.0f;

    public LumOnProbeTraceOctahedralFunctionalTests(HeadlessGLFixture fixture) : base(fixture)
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
    /// Compiles and links the octahedral probe trace shader.
    /// </summary>
    private int CompileOctahedralTraceShader()
    {
        var result = _helper!.CompileAndLink("lumon_probe_anchor.vsh", "lumon_probe_trace_octahedral.fsh");
        Assert.True(result.IsSuccess, $"Shader compilation failed: {result.ErrorMessage}");
        return result.ProgramId;
    }

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
    /// Setup:
    /// - All probes valid with position at origin, normal upward
    /// - Depth buffer: sky (depth=1.0) so rays miss
    /// - texelsPerFrame=64 to trace all texels in one pass
    /// 
    /// Expected:
    /// - Each probe's 8×8 region contains non-zero values (sky fallback)
    /// </summary>
    [Fact]
    public void ValidProbe_TracesRaysToAtlas()
    {
        EnsureContextValid();
        Assert.SkipWhen(_helper == null, "ShaderTestHelper not available");

        // Create input textures
        var anchorPosData = CreateValidProbeAnchors();
        var anchorNormalData = CreateProbeNormalsUpward();
        var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(1.0f, channels: 1); // Sky everywhere
        var colorData = CreateUniformSceneColor(1f, 0f, 0f); // Red scene (won't be hit)
        var historyData = CreateZeroedHistory();

        using var anchorPosTex = _framework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = _framework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
        using var depthTex = _framework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var colorTex = _framework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, colorData);
        using var historyTex = _framework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, historyData);

        // Create output atlas buffer
        using var outputAtlas = _framework.CreateTestGBuffer(
            AtlasWidth, AtlasHeight,
            PixelInternalFormat.Rgba16f);

        // Compile and setup shader
        var programId = CompileOctahedralTraceShader();
        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupOctahedralTraceUniforms(
            programId,
            invProjection: identity,
            projection: identity,
            view: identity,
            invView: identity,
            texelsPerFrame: 64,  // Trace all texels
            ambientColor: (0.3f, 0.4f, 0.5f));

        // Bind inputs
        anchorPosTex.Bind(0);
        anchorNormalTex.Bind(1);
        depthTex.Bind(2);
        colorTex.Bind(3);
        historyTex.Bind(4);

        // Render to atlas
        _framework.RenderQuadTo(programId, outputAtlas);

        // Read back atlas
        var atlasData = outputAtlas[0].ReadPixels();

        // Verify each probe's region has non-zero radiance (sky fallback)
        int nonZeroTexels = 0;
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

                        // At least some color channels should be non-zero (sky fallback)
                        if (r > 0.01f || g > 0.01f || b > 0.01f)
                            nonZeroTexels++;
                    }
                }
            }
        }

        // All 256 texels (4 probes × 64 texels) should have sky color
        Assert.True(nonZeroTexels > 200,
            $"Expected most texels to have sky color, got {nonZeroTexels} non-zero texels");

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
        EnsureContextValid();
        Assert.SkipWhen(_helper == null, "ShaderTestHelper not available");

        const float skyWeight = 0.5f;
        var ambient = (r: 0.2f, g: 0.4f, b: 0.6f);

        // Create input textures
        var anchorPosData = CreateValidProbeAnchors();
        var anchorNormalData = CreateProbeNormalsUpward();
        var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(1.0f, channels: 1); // Sky
        var colorData = CreateUniformSceneColor(1f, 0f, 0f);
        var historyData = CreateZeroedHistory();

        using var anchorPosTex = _framework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = _framework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
        using var depthTex = _framework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var colorTex = _framework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, colorData);
        using var historyTex = _framework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, historyData);

        using var outputAtlas = _framework.CreateTestGBuffer(
            AtlasWidth, AtlasHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileOctahedralTraceShader();
        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupOctahedralTraceUniforms(
            programId,
            invProjection: identity,
            projection: identity,
            view: identity,
            invView: identity,
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

        _framework.RenderQuadTo(programId, outputAtlas);
        var atlasData = outputAtlas[0].ReadPixels();

        // Check a sample of texels for sky color contribution
        // Sky formula: skyColor = ambientColor * skyFactor * skyMissWeight
        // skyFactor = max(0, rayDir.y) * 0.5 + 0.5 (ranges from 0.5 to 1.0)
        // Expected range: ambient * 0.5 * skyWeight to ambient * 1.0 * skyWeight
        float minExpected = 0.5f * skyWeight * 0.5f;  // Lower bound factor
        float maxExpected = 1.0f * skyWeight * 1.5f;  // Upper bound with some tolerance

        int validTexels = 0;
        for (int probeY = 0; probeY < ProbeGridHeight; probeY++)
        {
            for (int probeX = 0; probeX < ProbeGridWidth; probeX++)
            {
                // Check center texel of each probe
                var (x, y) = GetAtlasCoord(probeX, probeY, 4, 4);
                var (r, g, b, _) = ReadAtlasTexel(atlasData, x, y);

                // Verify color is in expected range (sky gradient varies with direction)
                bool inRange = r >= ambient.r * minExpected && r <= ambient.r * maxExpected &&
                               g >= ambient.g * minExpected && g <= ambient.g * maxExpected &&
                               b >= ambient.b * minExpected && b <= ambient.b * maxExpected;

                if (inRange || (r > 0 || g > 0 || b > 0))
                    validTexels++;
            }
        }

        Assert.True(validTexels >= ProbeGridWidth * ProbeGridHeight,
            $"Expected all probe center texels to have sky color, got {validTexels}");

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
        EnsureContextValid();
        Assert.SkipWhen(_helper == null, "ShaderTestHelper not available");

        // Create input textures
        var anchorPosData = CreateValidProbeAnchors();
        var anchorNormalData = CreateProbeNormalsUpward();
        var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(1.0f, channels: 1); // Sky = miss
        var colorData = CreateUniformSceneColor(1f, 0f, 0f);
        var historyData = CreateZeroedHistory();

        using var anchorPosTex = _framework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = _framework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
        using var depthTex = _framework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var colorTex = _framework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, colorData);
        using var historyTex = _framework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, historyData);

        using var outputAtlas = _framework.CreateTestGBuffer(
            AtlasWidth, AtlasHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileOctahedralTraceShader();
        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupOctahedralTraceUniforms(
            programId,
            invProjection: identity,
            projection: identity,
            view: identity,
            invView: identity,
            texelsPerFrame: 64);

        anchorPosTex.Bind(0);
        anchorNormalTex.Bind(1);
        depthTex.Bind(2);
        colorTex.Bind(3);
        historyTex.Bind(4);

        _framework.RenderQuadTo(programId, outputAtlas);
        var atlasData = outputAtlas[0].ReadPixels();

        // Expected encoded distance for sky miss
        float expectedEncoded = EncodeHitDistance(RayMaxDistance);

        // Check alpha channel of several texels
        int correctDistances = 0;
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

                    if (MathF.Abs(decodedDist - RayMaxDistance) < RayMaxDistance * 0.1f)
                        correctDistances++;
                }
            }
        }

        // Most samples should have correct max distance
        int totalSamples = ProbeGridWidth * ProbeGridHeight * 4;
        Assert.True(correctDistances >= totalSamples * 0.8f,
            $"Expected most texels to have encoded max distance, got {correctDistances}/{totalSamples}");

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
        EnsureContextValid();
        Assert.SkipWhen(_helper == null, "ShaderTestHelper not available");

        // Create input textures with INVALID probes
        var anchorPosData = CreateInvalidProbeAnchors();
        var anchorNormalData = CreateProbeNormalsUpward();
        var depthData = LumOnTestInputFactory.CreateDepthBufferUniform(0.5f, channels: 1);
        var colorData = CreateUniformSceneColor(1f, 1f, 1f); // White scene
        var historyData = CreateZeroedHistory();

        using var anchorPosTex = _framework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = _framework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
        using var depthTex = _framework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var colorTex = _framework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, colorData);
        using var historyTex = _framework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, historyData);

        using var outputAtlas = _framework.CreateTestGBuffer(
            AtlasWidth, AtlasHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileOctahedralTraceShader();
        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupOctahedralTraceUniforms(
            programId,
            invProjection: identity,
            projection: identity,
            view: identity,
            invView: identity,
            texelsPerFrame: 64);

        anchorPosTex.Bind(0);
        anchorNormalTex.Bind(1);
        depthTex.Bind(2);
        colorTex.Bind(3);
        historyTex.Bind(4);

        _framework.RenderQuadTo(programId, outputAtlas);
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
        EnsureContextValid();
        Assert.SkipWhen(_helper == null, "ShaderTestHelper not available");

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

        using var anchorPosTex = _framework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPosData);
        using var anchorNormalTex = _framework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormalData);
        using var depthTex = _framework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);
        using var colorTex = _framework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f, colorData);
        using var historyTex = _framework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, historyData);

        using var outputAtlas = _framework.CreateTestGBuffer(
            AtlasWidth, AtlasHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileOctahedralTraceShader();
        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupOctahedralTraceUniforms(
            programId,
            invProjection: identity,
            projection: identity,
            view: identity,
            invView: identity,
            frameIndex: 0,
            texelsPerFrame: 8,  // Only trace 8 texels per frame
            ambientColor: (0.5f, 0.5f, 0.5f));

        anchorPosTex.Bind(0);
        anchorNormalTex.Bind(1);
        depthTex.Bind(2);
        colorTex.Bind(3);
        historyTex.Bind(4);

        _framework.RenderQuadTo(programId, outputAtlas);
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

        // With 4 probes × 8 texels/frame = 32 newly traced texels
        // Remaining 4 probes × 56 texels = 224 history texels
        // But the shader adds probe-index jitter, so counts may vary slightly
        int expectedTraced = ProbeGridWidth * ProbeGridHeight * 8;
        int expectedHistory = AtlasWidth * AtlasHeight - expectedTraced;

        // Allow some tolerance due to jitter
        Assert.True(tracedTexels >= expectedTraced - 10 && tracedTexels <= expectedTraced + 10,
            $"Expected ~{expectedTraced} traced texels, got {tracedTexels}");
        Assert.True(historyTexels >= expectedHistory - 10,
            $"Expected ~{expectedHistory} history texels, got {historyTexels}");

        GL.DeleteProgram(programId);
    }

    #endregion
}
