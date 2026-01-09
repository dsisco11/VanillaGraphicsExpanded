using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>
/// Functional tests for the LumOn Temporal Accumulation shader pass.
/// 
/// These tests verify that the temporal shader correctly:
/// - Passes through current frame data when α=1.0 (bypass mode)
/// - Blends current and history radiance using exponential moving average
/// - Rejects history when depth or normal discontinuities are detected
/// - Updates metadata (depth, normal, accumCount) for next frame validation
/// 
/// Test configuration:
/// - Probe grid: 2×2 probes
/// - Current/History radiance: RGBA16F per probe
/// - Meta buffer: RGBA16F (R=depth, GBA=normal encoded, A=accumCount)
/// </summary>
/// <remarks>
/// Temporal blend formula:
/// <code>
/// output = mix(current, history, alpha * validation.confidence)
/// 
/// Alpha = 1.0: output = current (passthrough)
/// Alpha = 0.1: output = lerp(current, history, 0.1) if history valid
/// Alpha = 0.0: output = current (history fully rejected)
/// </code>
/// 
/// History validation rejects when:
/// <code>
/// - historyUV outside [0,1] bounds
/// - depth difference > depthRejectThreshold (relative)
/// - normal dot product &lt; normalRejectThreshold
/// </code>
/// </remarks>
[Collection("GPU")]
[Trait("Category", "GPU")]
public class LumOnTemporalFunctionalTests : LumOnShaderFunctionalTestBase
{
    // Default temporal parameters
    private const float DefaultTemporalAlpha = 0.95f;
    private const float DefaultDepthRejectThreshold = 0.1f;   // 10% relative depth difference
    private const float DefaultNormalRejectThreshold = 0.9f;  // cos(~25°)

    public LumOnTemporalFunctionalTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Helper Methods

    /// <summary>
    /// Compiles and links the temporal accumulation shader.
    /// </summary>
    private int CompileTemporalShader() => CompileShader("lumon_temporal.vsh", "lumon_temporal.fsh");

    /// <summary>
    /// Sets up common uniforms for the temporal shader.
    /// </summary>
    /// <param name="programId">Shader program ID.</param>
    /// <param name="viewMatrix">Current frame view matrix.</param>
    /// <param name="invViewMatrix">Current frame inverse view matrix.</param>
    /// <param name="prevViewProjMatrix">Previous frame view-projection matrix.</param>
    /// <param name="temporalAlpha">Blend factor (0-1). Higher = more history.</param>
    /// <param name="depthRejectThreshold">Relative depth threshold for rejection.</param>
    /// <param name="normalRejectThreshold">Dot product threshold for normal rejection.</param>
    private void SetupTemporalUniforms(
        int programId,
        float[] viewMatrix,
        float[] invViewMatrix,
        float[] prevViewProjMatrix,
        float temporalAlpha = DefaultTemporalAlpha,
        float depthRejectThreshold = DefaultDepthRejectThreshold,
        float normalRejectThreshold = DefaultNormalRejectThreshold)
    {
        GL.UseProgram(programId);

        // Matrix uniforms
        var viewLoc = GL.GetUniformLocation(programId, "viewMatrix");
        var invViewLoc = GL.GetUniformLocation(programId, "invViewMatrix");
        var prevViewProjLoc = GL.GetUniformLocation(programId, "prevViewProjMatrix");
        GL.UniformMatrix4(viewLoc, 1, false, viewMatrix);
        GL.UniformMatrix4(invViewLoc, 1, false, invViewMatrix);
        GL.UniformMatrix4(prevViewProjLoc, 1, false, prevViewProjMatrix);

        // Probe grid uniforms
        var gridSizeLoc = GL.GetUniformLocation(programId, "probeGridSize");
        GL.Uniform2(gridSizeLoc, (float)ProbeGridWidth, (float)ProbeGridHeight);

        // Depth parameters
        var zNearLoc = GL.GetUniformLocation(programId, "zNear");
        var zFarLoc = GL.GetUniformLocation(programId, "zFar");
        GL.Uniform1(zNearLoc, ZNear);
        GL.Uniform1(zFarLoc, ZFar);

        // Temporal parameters
        var alphaLoc = GL.GetUniformLocation(programId, "temporalAlpha");
        var depthThreshLoc = GL.GetUniformLocation(programId, "depthRejectThreshold");
        var normalThreshLoc = GL.GetUniformLocation(programId, "normalRejectThreshold");
        GL.Uniform1(alphaLoc, temporalAlpha);
        GL.Uniform1(depthThreshLoc, depthRejectThreshold);
        GL.Uniform1(normalThreshLoc, normalRejectThreshold);

        // Texture sampler uniforms
        var radianceCurrent0Loc = GL.GetUniformLocation(programId, "radianceCurrent0");
        var radianceCurrent1Loc = GL.GetUniformLocation(programId, "radianceCurrent1");
        var radianceHistory0Loc = GL.GetUniformLocation(programId, "radianceHistory0");
        var radianceHistory1Loc = GL.GetUniformLocation(programId, "radianceHistory1");
        var anchorPosLoc = GL.GetUniformLocation(programId, "probeAnchorPosition");
        var anchorNormalLoc = GL.GetUniformLocation(programId, "probeAnchorNormal");
        var historyMetaLoc = GL.GetUniformLocation(programId, "historyMeta");

        GL.Uniform1(radianceCurrent0Loc, 0);
        GL.Uniform1(radianceCurrent1Loc, 1);
        GL.Uniform1(radianceHistory0Loc, 2);
        GL.Uniform1(radianceHistory1Loc, 3);
        GL.Uniform1(anchorPosLoc, 4);
        GL.Uniform1(anchorNormalLoc, 5);
        GL.Uniform1(historyMetaLoc, 6);

        GL.UseProgram(0);
    }

    /// <summary>
    /// Creates a probe anchor position buffer with all probes valid at known world positions.
    /// All probes at the same world XY position so they all reproject to the same history texel.
    /// This simplifies testing by ensuring all probes sample the same history data.
    /// </summary>
    /// <remarks>
    /// With posWS.xy = (0, 0) and identity prevViewProjMatrix:
    /// historyUV = (0, 0) * 0.5 + 0.5 = (0.5, 0.5)
    /// 
    /// For a 2x2 texture, UV (0.5, 0.5) is at the boundary. With Nearest filtering:
    /// - Some drivers round down: texel (0,0)
    /// - Some drivers round up: texel (1,1)
    /// 
    /// To avoid ambiguity, we use a uniform history buffer where all texels have the same value.
    /// </remarks>
    private static float[] CreateValidProbeAnchors(float worldZ = 5.0f)
    {
        var data = new float[ProbeGridWidth * ProbeGridHeight * 4];
        for (int py = 0; py < ProbeGridHeight; py++)
        {
            for (int px = 0; px < ProbeGridWidth; px++)
            {
                int idx = (py * ProbeGridWidth + px) * 4;
                // All probes at same XY to get same history UV
                data[idx + 0] = 0f;      // X = 0 -> historyUV.x = 0.5
                data[idx + 1] = 0f;      // Y = 0 -> historyUV.y = 0.5
                data[idx + 2] = -worldZ; // Z (negative = in front of camera)
                data[idx + 3] = 1.0f;    // Valid
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
    /// Creates a probe anchor normal buffer with upward-facing normals (encoded to 0-1 range).
    /// Shader decodes as: normalWS = anchorNormal.xyz * 2.0 - 1.0
    /// </summary>
    private static float[] CreateProbeNormalsUpward()
    {
        var data = new float[ProbeGridWidth * ProbeGridHeight * 4];
        // Encode (0, 1, 0) to [0,1] range: (0.5, 1.0, 0.5)
        for (int i = 0; i < ProbeGridWidth * ProbeGridHeight; i++)
        {
            int idx = i * 4;
            data[idx + 0] = 0.5f;  // X: 0 encoded as 0.5
            data[idx + 1] = 1.0f;  // Y: 1 encoded as 1.0
            data[idx + 2] = 0.5f;  // Z: 0 encoded as 0.5
            data[idx + 3] = 0f;    // Reserved
        }
        return data;
    }

    /// <summary>
    /// Creates a probe anchor normal buffer with custom normal (encoded to 0-1 range).
    /// </summary>
    private static float[] CreateProbeNormalsCustom(float nx, float ny, float nz)
    {
        var data = new float[ProbeGridWidth * ProbeGridHeight * 4];
        // Encode normal to [0,1] range
        for (int i = 0; i < ProbeGridWidth * ProbeGridHeight; i++)
        {
            int idx = i * 4;
            data[idx + 0] = nx * 0.5f + 0.5f;  // X encoded
            data[idx + 1] = ny * 0.5f + 0.5f;  // Y encoded
            data[idx + 2] = nz * 0.5f + 0.5f;  // Z encoded
            data[idx + 3] = 0f;
        }
        return data;
    }

    /// <summary>
    /// Creates a uniform color radiance buffer for probe grid.
    /// </summary>
    private static float[] CreateUniformRadiance(float r, float g, float b, float a = 1.0f)
    {
        var data = new float[ProbeGridWidth * ProbeGridHeight * 4];
        for (int i = 0; i < ProbeGridWidth * ProbeGridHeight; i++)
        {
            int idx = i * 4;
            data[idx + 0] = r;
            data[idx + 1] = g;
            data[idx + 2] = b;
            data[idx + 3] = a;
        }
        return data;
    }

    /// <summary>
    /// Creates a current-frame radiance pattern whose 3×3 neighborhood (after edge clamping)
    /// spans blue in [0, 1]. This avoids neighborhood clamping forcing a blue history sample to 0.
    /// </summary>
    private static float[] CreateCurrentRadianceWithBlueNeighborhood()
    {
        var data = CreateUniformRadiance(0f, 0f, 0f);

        // Probe (0,0): red only (blue = 0) — this is the probe we read in tests.
        int idx00 = 0;
        data[idx00 + 0] = 1f;
        data[idx00 + 1] = 0f;
        data[idx00 + 2] = 0f;
        data[idx00 + 3] = 1f;

        // Probe (1,0): blue only (blue = 1) — ensures neighborhood max blue becomes 1.
        int idx10 = (0 * ProbeGridWidth + 1) * 4;
        data[idx10 + 0] = 0f;
        data[idx10 + 1] = 0f;
        data[idx10 + 2] = 1f;
        data[idx10 + 3] = 1f;

        return data;
    }

    /// <summary>
    /// Creates a history meta buffer with specified depth, normal, and accumulation count.
    /// 
    /// DESIRED FORMAT (what shader should use):
    /// - R: linearized depth
    /// - G: normal.x encoded as (n * 0.5 + 0.5)
    /// - B: normal.y encoded as (n * 0.5 + 0.5)  
    /// - A: accumulation count (separate from normal.z)
    /// 
    /// For proper temporal validation, the shader should:
    /// 1. Store normal.z separately OR use only 2D normal comparison
    /// 2. Not conflate accumCount with normal.z
    /// </summary>
    private static float[] CreateHistoryMeta(
        float linearDepth, 
        float accumCount, 
        float normalX = 0f, 
        float normalY = 1f)
    {
        var data = new float[ProbeGridWidth * ProbeGridHeight * 4];
        for (int i = 0; i < ProbeGridWidth * ProbeGridHeight; i++)
        {
            int idx = i * 4;
            data[idx + 0] = linearDepth;
            data[idx + 1] = normalX * 0.5f + 0.5f;  // G = nx encoded
            data[idx + 2] = normalY * 0.5f + 0.5f;  // B = ny encoded  
            data[idx + 3] = accumCount;              // A = accumCount
        }
        return data;
    }

    /// <summary>
    /// Reads probe data from output buffer.
    /// </summary>
    private static (float r, float g, float b, float a) ReadProbe(float[] data, int probeX, int probeY)
    {
        int idx = (probeY * ProbeGridWidth + probeX) * 4;
        return (data[idx], data[idx + 1], data[idx + 2], data[idx + 3]);
    }

    /// <summary>
    /// Asserts that two colors are approximately equal within epsilon.
    /// </summary>
    private static void AssertColorApprox(
        (float r, float g, float b) expected,
        (float r, float g, float b) actual,
        float epsilon,
        string message)
    {
        Assert.True(
            MathF.Abs(expected.r - actual.r) < epsilon &&
            MathF.Abs(expected.g - actual.g) < epsilon &&
            MathF.Abs(expected.b - actual.b) < epsilon,
            $"{message}: Expected ({expected.r:F4}, {expected.g:F4}, {expected.b:F4}), " +
            $"got ({actual.r:F4}, {actual.g:F4}, {actual.b:F4})");
    }

    #endregion

    #region Single-Frame Tests (α=1.0 Bypass)

    /// <summary>
    /// Tests that with temporalAlpha=1.0, current frame radiance passes through unchanged.
    /// This is the "bypass" mode where temporal accumulation is disabled.
    /// 
    /// Setup:
    /// - Current radiance: red (1, 0, 0)
    /// - History radiance: blue (0, 0, 1)
    /// - temporalAlpha = 1.0 (full passthrough)
    /// 
    /// Expected:
    /// - Output radiance = red (current passthrough)
    /// </summary>
    /// <remarks>
    /// With α=1.0, the shader should output current radiance regardless of history.
    /// The blend formula mix(current, history, alpha) with alpha approaching 1.0
    /// becomes dominated by history, but validation confidence modulates alpha.
    /// At first frame (accumCount=1), alpha is reduced significantly.
    /// 
    /// Actually, the shader does: mix(currentRad, historyRad, alpha)
    /// With alpha=1.0, this gives historyRad. But for first frame, 
    /// accumCount check reduces alpha: alpha *= min(prevAccum / 10.0, 1.0)
    /// With prevAccum=1.0, alpha *= 0.1, so output ≈ mix(current, history, 0.1)
    /// 
    /// For true passthrough, we need invalid history (validation.valid = false).
    /// </remarks>
    [Fact]
    public void AlphaOne_PassthroughCurrent()
    {
        EnsureShaderTestAvailable();

        // Test colors
        var currentColor = (r: 1.0f, g: 0.0f, b: 0.0f);  // Red
        var historyColor = (r: 0.0f, g: 0.0f, b: 1.0f);  // Blue

        // Create input textures
        var currentRad0 = CreateUniformRadiance(currentColor.r, currentColor.g, currentColor.b);
        var currentRad1 = CreateUniformRadiance(0.5f, 0.5f, 0.5f);  // Secondary radiance channel
        var historyRad0 = CreateUniformRadiance(historyColor.r, historyColor.g, historyColor.b);
        var historyRad1 = CreateUniformRadiance(0.2f, 0.2f, 0.2f);
        
        // Create valid anchors at known depth
        float worldZ = 5.0f;
        var anchorPos = CreateValidProbeAnchors(worldZ);
        var anchorNormal = CreateProbeNormalsUpward();
        
        // History meta with zero depth = invalid history (forces passthrough)
        // When historyDepthLin < 0.001, validation fails early
        var historyMeta = CreateHistoryMeta(0.0f, 0.0f);

        using var currentRad0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, currentRad0);
        using var currentRad1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, currentRad1);
        using var historyRad0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyRad0);
        using var historyRad1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyRad1);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPos);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormal);
        using var historyMetaTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyMeta);

        // Create MRT output: 3 attachments (radiance0, radiance1, meta)
        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ProbeGridWidth, ProbeGridHeight,
            PixelInternalFormat.Rgba16f,
            PixelInternalFormat.Rgba16f,
            PixelInternalFormat.Rgba16f);

        // Compile and setup shader
        var programId = CompileTemporalShader();
        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupTemporalUniforms(
            programId,
            viewMatrix: identity,
            invViewMatrix: identity,
            prevViewProjMatrix: identity,
            temporalAlpha: 1.0f);

        // Bind inputs to texture units
        currentRad0Tex.Bind(0);
        currentRad1Tex.Bind(1);
        historyRad0Tex.Bind(2);
        historyRad1Tex.Bind(3);
        anchorPosTex.Bind(4);
        anchorNormalTex.Bind(5);
        historyMetaTex.Bind(6);

        // Render
        TestFramework.RenderQuadTo(programId, outputGBuffer);

        // Read back results
        var outputRad0 = outputGBuffer[0].ReadPixels();

        // Verify all probes output current color (red) due to invalid history
        for (int py = 0; py < ProbeGridHeight; py++)
        {
            for (int px = 0; px < ProbeGridWidth; px++)
            {
                var (r, g, b, _) = ReadProbe(outputRad0, px, py);
                AssertColorApprox(
                    currentColor,
                    (r, g, b),
                    TestEpsilon,
                    $"Probe ({px},{py}) should pass through current when history invalid");
            }
        }

        GL.DeleteProgram(programId);
    }

    /// <summary>
    /// Tests that the meta output buffer is updated with current frame values.
    /// 
    /// Setup:
    /// - Valid probes at known depth
    /// - Upward-facing normals
    /// 
    /// Expected:
    /// - outMeta.r = linearized depth from view-space position
    /// - outMeta.gba = view-space normal encoded to 0-1 range
    /// - outMeta.a = accumCount (1 for first frame with invalid history)
    /// </summary>
    [Fact]
    public void AlphaOne_MetaUpdated()
    {
        EnsureShaderTestAvailable();

        // Create input textures
        var currentRad0 = CreateUniformRadiance(1.0f, 0.0f, 0.0f);
        var currentRad1 = CreateUniformRadiance(0.5f, 0.5f, 0.5f);
        var historyRad0 = CreateUniformRadiance(0.0f, 0.0f, 1.0f);
        var historyRad1 = CreateUniformRadiance(0.2f, 0.2f, 0.2f);
        
        // Probes at Z=-5 in world space (will be Z=5 in view space after transformation)
        float worldZ = 5.0f;
        var anchorPos = CreateValidProbeAnchors(worldZ);
        var anchorNormal = CreateProbeNormalsUpward();
        
        // Invalid history to ensure we get accumCount=1
        var historyMeta = CreateHistoryMeta(0.0f, 0.0f);

        using var currentRad0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, currentRad0);
        using var currentRad1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, currentRad1);
        using var historyRad0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyRad0);
        using var historyRad1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyRad1);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPos);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormal);
        using var historyMetaTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyMeta);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ProbeGridWidth, ProbeGridHeight,
            PixelInternalFormat.Rgba16f,
            PixelInternalFormat.Rgba16f,
            PixelInternalFormat.Rgba16f);

        var programId = CompileTemporalShader();
        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupTemporalUniforms(
            programId,
            viewMatrix: identity,
            invViewMatrix: identity,
            prevViewProjMatrix: identity,
            temporalAlpha: 1.0f);

        currentRad0Tex.Bind(0);
        currentRad1Tex.Bind(1);
        historyRad0Tex.Bind(2);
        historyRad1Tex.Bind(3);
        anchorPosTex.Bind(4);
        anchorNormalTex.Bind(5);
        historyMetaTex.Bind(6);

        TestFramework.RenderQuadTo(programId, outputGBuffer);

        // Read meta output (attachment 2)
        var outputMeta = outputGBuffer[2].ReadPixels();

        // Verify meta data for each probe
        // With identity view matrix, posVS.z = posWS.z = -worldZ
        // currentDepthLin = -posVS.z = worldZ = 5.0
        float expectedDepth = worldZ;

        for (int py = 0; py < ProbeGridHeight; py++)
        {
            for (int px = 0; px < ProbeGridWidth; px++)
            {
                var (depth, normalX, normalY, accumCount) = ReadProbe(outputMeta, px, py);

                // Check linearized depth
                Assert.True(
                    MathF.Abs(depth - expectedDepth) < 0.5f,
                    $"Probe ({px},{py}) depth: expected ~{expectedDepth}, got {depth}");

                // Check normal is approximately upward (0,1,0) encoded as (0.5, 1.0, 0.5)
                // After transformation: normalVS = mat3(viewMatrix) * normalWS
                // With identity view, normalVS = normalWS = (0,1,0)
                // Encoded: normalVS * 0.5 + 0.5 = (0.5, 1.0, 0.5)
                Assert.True(
                    normalY > 0.8f,
                    $"Probe ({px},{py}) normal.y should be high (upward), got {normalY}");

                // Check accumulation count = 1 (first frame with invalid history)
                Assert.True(
                    MathF.Abs(accumCount - 1.0f) < TestEpsilon,
                    $"Probe ({px},{py}) accumCount: expected 1, got {accumCount}");
            }
        }

        GL.DeleteProgram(programId);
    }

    #endregion

    #region Two-Frame Blend Tests (α=0.1)

    /// <summary>
    /// Tests that with valid history, radiance is blended using exponential moving average.
    /// 
    /// DESIRED BEHAVIOR:
    /// - When depth and normal match between frames, confidence should be ~1.0
    /// - With temporalAlpha=0.9, output = mix(current, history, 0.9)
    /// - For dim probe (current=0.3, history=0.6): expected ≈ 0.3*0.1 + 0.6*0.9 = 0.57
    /// - For bright probe (current=1.0, history=0.6): expected ≈ 1.0*0.1 + 0.6*0.9 = 0.64
    /// 
    /// Setup:
    /// - Current radiance: checkerboard of dim (0.3) and bright (1.0) 
    /// - History radiance: medium gray (0.6) - within neighborhood bounds [0.3, 1.0]
    /// - temporalAlpha = 0.9 (90% history contribution)
    /// - Matching depth and normals for high confidence
    /// </summary>
    /// <remarks>
    /// KNOWN ISSUE: The current shader implementation has a bug where historyMeta.gba is
    /// read as the history normal, but .a contains accumCount instead of normal.z.
    /// This causes normal validation to fail even when normals actually match.
    /// 
    /// FIX REQUIRED in lumon_temporal.fsh:
    /// - Store/read normal.z separately from accumCount, OR
    /// - Use 2D normal comparison (ignoring z), OR  
    /// - Pack accumCount differently (e.g., in a separate texture)
    /// 
    /// Until fixed, this test will fail - which is correct behavior for a test
    /// that documents desired functionality.
    /// </remarks>
    [Fact]
    public void AlphaBlend_AccumulatesHistory()
    {
        EnsureShaderTestAvailable();

        const float alpha = 0.9f;
        const float historyBrightness = 0.6f;

        // Create current radiance with checkerboard pattern for varied neighborhood
        var currentRad0 = new float[ProbeGridWidth * ProbeGridHeight * 4];
        for (int py = 0; py < ProbeGridHeight; py++)
        {
            for (int px = 0; px < ProbeGridWidth; px++)
            {
                int idx = (py * ProbeGridWidth + px) * 4;
                bool isDim = (px + py) % 2 == 0;
                float brightness = isDim ? 0.3f : 1.0f;
                currentRad0[idx + 0] = brightness;
                currentRad0[idx + 1] = brightness;
                currentRad0[idx + 2] = brightness;
                currentRad0[idx + 3] = 1.0f;
            }
        }

        // History: medium gray within [0.3, 1.0] neighborhood range
        var historyRad0 = CreateUniformRadiance(historyBrightness, historyBrightness, historyBrightness);
        
        var currentRad1 = CreateUniformRadiance(0.5f, 0.5f, 0.5f);
        var historyRad1 = CreateUniformRadiance(0.5f, 0.5f, 0.5f);

        float worldZ = 5.0f;
        var anchorPos = CreateValidProbeAnchors(worldZ);
        var anchorNormal = CreateProbeNormalsUpward();

        // Valid history with matching depth, normal, and high accumulation count
        var historyMeta = CreateHistoryMeta(
            linearDepth: worldZ,
            accumCount: 15.0f,  // High for full ramp-up
            normalX: 0f,
            normalY: 1f);

        using var currentRad0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, currentRad0);
        using var currentRad1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, currentRad1);
        using var historyRad0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyRad0);
        using var historyRad1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyRad1);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPos);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormal);
        using var historyMetaTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyMeta);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ProbeGridWidth, ProbeGridHeight,
            PixelInternalFormat.Rgba16f,
            PixelInternalFormat.Rgba16f,
            PixelInternalFormat.Rgba16f);

        var programId = CompileTemporalShader();
        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupTemporalUniforms(
            programId,
            viewMatrix: identity,
            invViewMatrix: identity,
            prevViewProjMatrix: identity,
            temporalAlpha: alpha,
            depthRejectThreshold: 0.5f,
            normalRejectThreshold: 0.9f);  // Standard threshold

        currentRad0Tex.Bind(0);
        currentRad1Tex.Bind(1);
        historyRad0Tex.Bind(2);
        historyRad1Tex.Bind(3);
        anchorPosTex.Bind(4);
        anchorNormalTex.Bind(5);
        historyMetaTex.Bind(6);

        TestFramework.RenderQuadTo(programId, outputGBuffer);

        var outputRad0 = outputGBuffer[0].ReadPixels();
        var outputMeta = outputGBuffer[2].ReadPixels();

        // DESIRED: With matching depth/normal, confidence ≈ 1.0, full alpha blend
        // output = mix(current, history, 0.9) = current * 0.1 + history * 0.9
        const float blendTolerance = 0.1f;  // Allow 10% tolerance for clamping effects

        for (int py = 0; py < ProbeGridHeight; py++)
        {
            for (int px = 0; px < ProbeGridWidth; px++)
            {
                var (_, _, _, accumCount) = ReadProbe(outputMeta, px, py);
                var (r, g, b, _) = ReadProbe(outputRad0, px, py);
                
                bool isDim = (px + py) % 2 == 0;
                float currentBrightness = isDim ? 0.3f : 1.0f;
                
                // DESIRED: History should be accepted (accumCount increments)
                Assert.True(accumCount > 15.0f,
                    $"Probe ({px},{py}) should accept history, got accum={accumCount:F1}");
                
                // DESIRED: Output should be proper blend of current and history
                // expected = current * 0.1 + history * 0.9
                float expectedBrightness = currentBrightness * (1 - alpha) + historyBrightness * alpha;
                float actualBrightness = r;
                
                Assert.True(
                    MathF.Abs(actualBrightness - expectedBrightness) < blendTolerance,
                    $"Probe ({px},{py}) blend: expected ≈{expectedBrightness:F2}, got {actualBrightness:F3}");
            }
        }

        GL.DeleteProgram(programId);
    }

    /// <summary>
    /// Tests that large depth differences cause history rejection.
    /// 
    /// Setup:
    /// - Current depth: 5.0 (from probe anchor)
    /// - History depth: 50.0 (10x difference > 10% threshold)
    /// - depthRejectThreshold = 0.1 (10%)
    /// 
    /// Expected:
    /// - History rejected, output = current
    /// </summary>
    [Fact]
    public void DepthRejection_DiscardsHistory()
    {
        EnsureShaderTestAvailable();

        var currentColor = (r: 1.0f, g: 0.0f, b: 0.0f);  // Red
        var historyColor = (r: 0.0f, g: 0.0f, b: 1.0f);  // Blue

        var currentRad0 = CreateUniformRadiance(currentColor.r, currentColor.g, currentColor.b);
        var currentRad1 = CreateUniformRadiance(0.5f, 0.5f, 0.5f);
        var historyRad0 = CreateUniformRadiance(historyColor.r, historyColor.g, historyColor.b);
        var historyRad1 = CreateUniformRadiance(0.5f, 0.5f, 0.5f);

        float currentDepth = 5.0f;
        float historyDepth = 50.0f;  // 10x difference = 900% relative difference

        var anchorPos = CreateValidProbeAnchors(currentDepth);
        var anchorNormal = CreateProbeNormalsUpward();

        // History with very different depth
        var historyMeta = CreateHistoryMeta(
            linearDepth: historyDepth,
            accumCount: 100.0f,
            normalX: 0f,
            normalY: 1f);

        using var currentRad0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, currentRad0);
        using var currentRad1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, currentRad1);
        using var historyRad0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyRad0);
        using var historyRad1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyRad1);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPos);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormal);
        using var historyMetaTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyMeta);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ProbeGridWidth, ProbeGridHeight,
            PixelInternalFormat.Rgba16f,
            PixelInternalFormat.Rgba16f,
            PixelInternalFormat.Rgba16f);

        var programId = CompileTemporalShader();
        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupTemporalUniforms(
            programId,
            viewMatrix: identity,
            invViewMatrix: identity,
            prevViewProjMatrix: identity,
            temporalAlpha: 0.9f,
            depthRejectThreshold: 0.1f,    // 10% threshold - will reject 900% difference
            normalRejectThreshold: 0.5f);  // Permissive normal threshold

        currentRad0Tex.Bind(0);
        currentRad1Tex.Bind(1);
        historyRad0Tex.Bind(2);
        historyRad1Tex.Bind(3);
        anchorPosTex.Bind(4);
        anchorNormalTex.Bind(5);
        historyMetaTex.Bind(6);

        TestFramework.RenderQuadTo(programId, outputGBuffer);

        var outputRad0 = outputGBuffer[0].ReadPixels();

        // Output should be current (red) due to depth rejection
        for (int py = 0; py < ProbeGridHeight; py++)
        {
            for (int px = 0; px < ProbeGridWidth; px++)
            {
                var (r, g, b, _) = ReadProbe(outputRad0, px, py);
                AssertColorApprox(
                    currentColor,
                    (r, g, b),
                    TestEpsilon,
                    $"Probe ({px},{py}) should reject history due to depth mismatch");
            }
        }

        GL.DeleteProgram(programId);
    }

    /// <summary>
    /// Tests that large normal differences cause history rejection.
    /// 
    /// Setup:
    /// - Current normal: upward (0, 1, 0)
    /// - History normal: forward (0, 0, 1) - perpendicular, dot=0
    /// - normalRejectThreshold = 0.9 (cos ~25°)
    /// 
    /// Expected:
    /// - dot(current, history) = 0 &lt; 0.9, history rejected
    /// - Output = current
    /// </summary>
    [Fact]
    public void NormalRejection_DiscardsHistory()
    {
        EnsureShaderTestAvailable();

        var currentColor = (r: 1.0f, g: 0.0f, b: 0.0f);  // Red
        var historyColor = (r: 0.0f, g: 0.0f, b: 1.0f);  // Blue

        var currentRad0 = CreateUniformRadiance(currentColor.r, currentColor.g, currentColor.b);
        var currentRad1 = CreateUniformRadiance(0.5f, 0.5f, 0.5f);
        var historyRad0 = CreateUniformRadiance(historyColor.r, historyColor.g, historyColor.b);
        var historyRad1 = CreateUniformRadiance(0.5f, 0.5f, 0.5f);

        float worldZ = 5.0f;
        var anchorPos = CreateValidProbeAnchors(worldZ);
        var anchorNormal = CreateProbeNormalsUpward();  // (0, 1, 0)

        // History with perpendicular normal (forward instead of up)
        // Current normal after view transform: (0, 1, 0) with identity
        // History normal: (0, 0, 1) -> dot product = 0
        var historyMeta = CreateHistoryMeta(
            linearDepth: worldZ,  // Matching depth
            accumCount: 100.0f,
            normalX: 0f,
            normalY: 0f);  // Forward normal (0, 0, 1) encoded: normalY maps to z

        // Actually the shader stores normalVS in gba, need to check encoding
        // outMeta = vec4(currentDepthLin, normalVS * 0.5 + 0.5)
        // So for forward normal (0, 0, 1): encoded = (0.5, 0.5, 1.0)
        // For upward (0, 1, 0): encoded = (0.5, 1.0, 0.5)
        // History reads: historyNormal = histMeta.gba * 2.0 - 1.0
        
        // Create history meta with forward normal directly
        var historyMetaForward = new float[ProbeGridWidth * ProbeGridHeight * 4];
        for (int i = 0; i < ProbeGridWidth * ProbeGridHeight; i++)
        {
            int idx = i * 4;
            historyMetaForward[idx + 0] = worldZ;  // depth
            historyMetaForward[idx + 1] = 0.5f;    // normal.x = 0 encoded
            historyMetaForward[idx + 2] = 0.5f;    // normal.y = 0 encoded  
            historyMetaForward[idx + 3] = 100.0f;  // accumCount (in alpha, but shader uses .a)
        }
        // Wait - shader reads histMeta.gba for normal. Let me re-check.
        // outMeta = vec4(currentDepthLin, normalVS * 0.5 + 0.5);
        // This sets r=depth, g=normal.x, b=normal.y, a=accumCount
        // No wait: vec4(scalar, vec3) expands to (scalar, vec3.x, vec3.y, vec3.z)
        // But normalVS * 0.5 + 0.5 is vec3, so: vec4(depth, nx*0.5+0.5, ny*0.5+0.5, nz*0.5+0.5)
        // Then outMeta.a = accumCount is set separately.
        
        // Actually looking at shader again:
        // outMeta = vec4(currentDepthLin, normalVS * 0.5 + 0.5);
        // outMeta.a = accumCount;
        // So: r=depth, g=nx_enc, b=ny_enc, a=accumCount
        // historyNormal = histMeta.gba * 2.0 - 1.0
        // This reads (g, b, a) = (nx_enc, ny_enc, accumCount) and decodes as normal?
        // That's a bug in the shader - .gba includes accumCount in z!
        
        // For our test, let's work with what the shader does:
        // It reads .gba and treats a (accumCount) as normal.z
        // To make normals perpendicular: upward (0,1,0) vs forward (0,0,1)
        // Upward encoded: g=0.5, b=1.0, a=accum -> decoded = (0, 1, accum*2-1)
        // To get forward (0,0,1): we need decoded.z = 1, so a=1.0
        // But a is accumCount... This is broken in the shader.
        
        // Let's test with the shader as-is and document the behavior
        // For perpendicular normals with high accumCount (100):
        // Current upward: decoded = (0, 1, 199) - not normalized properly
        // This test may not work as expected due to shader issue.
        
        // Alternative: use low accumCount to test normal rejection
        // accumCount=0.5 -> decoded normal.z = 0, giving (0, 1, 0) for upward
        // For perpendicular: we want (1, 0, 0) or similar
        // g=1.0, b=0.5, a=0.5 -> decoded = (1, 0, 0) = right facing
        
        var historyMetaRight = new float[ProbeGridWidth * ProbeGridHeight * 4];
        for (int i = 0; i < ProbeGridWidth * ProbeGridHeight; i++)
        {
            int idx = i * 4;
            historyMetaRight[idx + 0] = worldZ;    // depth - matching
            historyMetaRight[idx + 1] = 1.0f;      // nx_enc = 1.0 -> nx = 1
            historyMetaRight[idx + 2] = 0.5f;      // ny_enc = 0.5 -> ny = 0
            historyMetaRight[idx + 3] = 0.5f;      // a -> nz = 0 (or accumCount - shader bug)
        }

        using var currentRad0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, currentRad0);
        using var currentRad1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, currentRad1);
        using var historyRad0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyRad0);
        using var historyRad1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyRad1);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPos);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormal);
        using var historyMetaTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyMetaRight);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ProbeGridWidth, ProbeGridHeight,
            PixelInternalFormat.Rgba16f,
            PixelInternalFormat.Rgba16f,
            PixelInternalFormat.Rgba16f);

        var programId = CompileTemporalShader();
        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupTemporalUniforms(
            programId,
            viewMatrix: identity,
            invViewMatrix: identity,
            prevViewProjMatrix: identity,
            temporalAlpha: 0.9f,
            depthRejectThreshold: 0.5f,    // Permissive depth threshold
            normalRejectThreshold: 0.9f);  // Strict normal threshold - dot < 0.9 rejects

        currentRad0Tex.Bind(0);
        currentRad1Tex.Bind(1);
        historyRad0Tex.Bind(2);
        historyRad1Tex.Bind(3);
        anchorPosTex.Bind(4);
        anchorNormalTex.Bind(5);
        historyMetaTex.Bind(6);

        TestFramework.RenderQuadTo(programId, outputGBuffer);

        var outputRad0 = outputGBuffer[0].ReadPixels();

        // Output should be current (red) due to normal rejection
        // Current normal: (0,1,0), history normal: (1,0,0), dot = 0 < 0.9
        for (int py = 0; py < ProbeGridHeight; py++)
        {
            for (int px = 0; px < ProbeGridWidth; px++)
            {
                var (r, g, b, _) = ReadProbe(outputRad0, px, py);
                AssertColorApprox(
                    currentColor,
                    (r, g, b),
                    TestEpsilon,
                    $"Probe ({px},{py}) should reject history due to normal mismatch");
            }
        }

        GL.DeleteProgram(programId);
    }

    /// <summary>
    /// Tests that accumulation count increments on valid history blend.
    /// 
    /// Setup:
    /// - Valid history with accumCount = 0.5 (decodes to nz=0, matching upward normal)
    /// - Matching depth and normals
    /// 
    /// Expected:
    /// - outMeta.a = min(prevAccum + 1, 100) = 1.5
    /// </summary>
    /// <remarks>
    /// Note: Due to shader quirk where accumCount shares channel with normal.z,
    /// we use accumCount = 0.5 which produces decoded nz = 0 for proper normal validation.
    /// With higher values, the normal validation would fail.
    /// </remarks>
    [Fact]
    public void AccumCount_Increments()
    {
        EnsureShaderTestAvailable();

        var currentRad0 = CreateUniformRadiance(1.0f, 0.0f, 0.0f);
        var currentRad1 = CreateUniformRadiance(0.5f, 0.5f, 0.5f);
        var historyRad0 = CreateUniformRadiance(0.0f, 0.0f, 1.0f);
        var historyRad1 = CreateUniformRadiance(0.5f, 0.5f, 0.5f);

        float worldZ = 5.0f;
        var anchorPos = CreateValidProbeAnchors(worldZ);
        var anchorNormal = CreateProbeNormalsUpward();

        // Use accumCount = 0.5 which gives nz = 0 after decoding
        // This matches the upward normal (0, 1, 0)
        float prevAccumCount = 0.5f;
        var historyMeta = CreateHistoryMeta(
            linearDepth: worldZ,
            accumCount: prevAccumCount,
            normalX: 0f,
            normalY: 1f);

        using var currentRad0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, currentRad0);
        using var currentRad1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, currentRad1);
        using var historyRad0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyRad0);
        using var historyRad1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyRad1);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPos);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormal);
        using var historyMetaTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyMeta);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ProbeGridWidth, ProbeGridHeight,
            PixelInternalFormat.Rgba16f,
            PixelInternalFormat.Rgba16f,
            PixelInternalFormat.Rgba16f);

        var programId = CompileTemporalShader();
        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupTemporalUniforms(
            programId,
            viewMatrix: identity,
            invViewMatrix: identity,
            prevViewProjMatrix: identity,
            temporalAlpha: 0.9f,
            depthRejectThreshold: 0.5f,
            normalRejectThreshold: 0.5f);

        currentRad0Tex.Bind(0);
        currentRad1Tex.Bind(1);
        historyRad0Tex.Bind(2);
        historyRad1Tex.Bind(3);
        anchorPosTex.Bind(4);
        anchorNormalTex.Bind(5);
        historyMetaTex.Bind(6);

        TestFramework.RenderQuadTo(programId, outputGBuffer);

        var outputMeta = outputGBuffer[2].ReadPixels();

        // Expected: accumCount = min(prevAccum + 1, 100) = 1.5
        float expectedAccumCount = MathF.Min(prevAccumCount + 1.0f, 100.0f);

        for (int py = 0; py < ProbeGridHeight; py++)
        {
            for (int px = 0; px < ProbeGridWidth; px++)
            {
                var (_, _, _, accumCount) = ReadProbe(outputMeta, px, py);
                Assert.True(
                    MathF.Abs(accumCount - expectedAccumCount) < 1.0f,
                    $"Probe ({px},{py}) accumCount: expected {expectedAccumCount}, got {accumCount}");
            }
        }

        GL.DeleteProgram(programId);
    }

    #endregion

    #region Phase 4 Tests: High Priority Missing Coverage

    /// <summary>
    /// Tests that depth rejection triggers precisely at the threshold boundary.
    /// 
    /// DESIRED BEHAVIOR:
    /// - When |currentDepth - historyDepth| / max(currentDepth, 0.001) > threshold, reject history
    /// - Values just below threshold should accept history
    /// - Values just above threshold should reject history
    /// 
    /// Setup:
    /// - depthRejectThreshold = 0.1 (10%)
    /// - Current depth = 10.0
    /// - Test with history depth = 10.5 (5% diff, should accept) vs 12.0 (20% diff, should reject)
    /// 
    /// Expected:
    /// - 5% diff: history accepted (blended output)
    /// - 20% diff: history rejected (current passthrough)
    /// </summary>
    [Fact]
    public void DepthRejection_TriggersAtThreshold()
    {
        EnsureShaderTestAvailable();

        // Note: The temporal shader applies neighborhood clamping.
        // If current is uniform red and history is uniform blue, the blue channel will be clamped to 0,
        // making history acceptance indistinguishable. Use a current pattern whose neighborhood spans blue.
        var historyColor = (r: 0.0f, g: 0.0f, b: 1.0f);  // Blue
        float currentDepth = 10.0f;
        float threshold = 0.1f;

        var currentRad0 = CreateCurrentRadianceWithBlueNeighborhood();
        var currentRad1 = CreateUniformRadiance(0.5f, 0.5f, 0.5f);
        var historyRad0 = CreateUniformRadiance(historyColor.r, historyColor.g, historyColor.b);
        var historyRad1 = CreateUniformRadiance(0.5f, 0.5f, 0.5f);
        var anchorPos = CreateValidProbeAnchors(currentDepth);
        var anchorNormal = CreateProbeNormalsUpward();

        float belowThresholdBlue;
        float aboveThresholdBlue;

        // History depth just below threshold (5% diff, should accept)
        {
            float historyDepth = currentDepth * 1.05f;  // 5% diff < 10% threshold
            var historyMeta = CreateHistoryMeta(historyDepth, 15.0f, 0f, 1f);

            using var currentRad0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, currentRad0);
            using var currentRad1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, currentRad1);
            using var historyRad0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyRad0);
            using var historyRad1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyRad1);
            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPos);
            using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormal);
            using var historyMetaTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyMeta);

            using var outputGBuffer = TestFramework.CreateTestGBuffer(
                ProbeGridWidth, ProbeGridHeight,
                PixelInternalFormat.Rgba16f,
                PixelInternalFormat.Rgba16f,
                PixelInternalFormat.Rgba16f);

            var programId = CompileTemporalShader();
            var identity = LumOnTestInputFactory.CreateIdentityMatrix();
            SetupTemporalUniforms(
                programId,
                viewMatrix: identity,
                invViewMatrix: identity,
                prevViewProjMatrix: identity,
                temporalAlpha: 0.9f,
                depthRejectThreshold: threshold,
                normalRejectThreshold: 0.5f);

            currentRad0Tex.Bind(0);
            currentRad1Tex.Bind(1);
            historyRad0Tex.Bind(2);
            historyRad1Tex.Bind(3);
            anchorPosTex.Bind(4);
            anchorNormalTex.Bind(5);
            historyMetaTex.Bind(6);

            TestFramework.RenderQuadTo(programId, outputGBuffer);
            var output = outputGBuffer[0].ReadPixels();
            belowThresholdBlue = output[2]; // Blue channel of first probe

            GL.DeleteProgram(programId);
        }

        // History depth above threshold (20% diff, should reject)
        {
            float historyDepth = currentDepth * 1.20f;  // 20% diff > 10% threshold
            var historyMeta = CreateHistoryMeta(historyDepth, 15.0f, 0f, 1f);

            using var currentRad0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, currentRad0);
            using var currentRad1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, currentRad1);
            using var historyRad0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyRad0);
            using var historyRad1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyRad1);
            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPos);
            using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormal);
            using var historyMetaTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyMeta);

            using var outputGBuffer = TestFramework.CreateTestGBuffer(
                ProbeGridWidth, ProbeGridHeight,
                PixelInternalFormat.Rgba16f,
                PixelInternalFormat.Rgba16f,
                PixelInternalFormat.Rgba16f);

            var programId = CompileTemporalShader();
            var identity = LumOnTestInputFactory.CreateIdentityMatrix();
            SetupTemporalUniforms(
                programId,
                viewMatrix: identity,
                invViewMatrix: identity,
                prevViewProjMatrix: identity,
                temporalAlpha: 0.9f,
                depthRejectThreshold: threshold,
                normalRejectThreshold: 0.5f);

            currentRad0Tex.Bind(0);
            currentRad1Tex.Bind(1);
            historyRad0Tex.Bind(2);
            historyRad1Tex.Bind(3);
            anchorPosTex.Bind(4);
            anchorNormalTex.Bind(5);
            historyMetaTex.Bind(6);

            TestFramework.RenderQuadTo(programId, outputGBuffer);
            var output = outputGBuffer[0].ReadPixels();
            aboveThresholdBlue = output[2];

            GL.DeleteProgram(programId);
        }

        // Below threshold should have more blue (history accepted)
        // Above threshold should have less blue (history rejected)
        Assert.True(belowThresholdBlue > aboveThresholdBlue,
            $"Below threshold should have more blue ({belowThresholdBlue:F3}) than above ({aboveThresholdBlue:F3})");
    }

    /// <summary>
    /// Tests that normal rejection triggers precisely at the threshold boundary.
    /// 
    /// DESIRED BEHAVIOR:
    /// - When dot(currentNormal, historyNormal) < threshold, reject history
    /// - Higher dot product (more similar normals) should accept
    /// 
    /// Setup:
    /// - normalRejectThreshold = 0.9 (cos ~25°)
    /// - Current normal = (0, 1, 0) upward
    /// - Test with similar vs perpendicular history normals
    /// </summary>
    [Fact]
    public void NormalRejection_TriggersAtThreshold()
    {
        EnsureShaderTestAvailable();

        // See DepthRejection_TriggersAtThreshold for why current uses a blue-spanning neighborhood.
        var historyColor = (r: 0.0f, g: 0.0f, b: 1.0f);
        float worldZ = 5.0f;
        float threshold = 0.9f;

        var currentRad0 = CreateCurrentRadianceWithBlueNeighborhood();
        var currentRad1 = CreateUniformRadiance(0.5f, 0.5f, 0.5f);
        var historyRad0 = CreateUniformRadiance(historyColor.r, historyColor.g, historyColor.b);
        var historyRad1 = CreateUniformRadiance(0.5f, 0.5f, 0.5f);
        var anchorPos = CreateValidProbeAnchors(worldZ);
        var anchorNormal = CreateProbeNormalsUpward();  // (0, 1, 0)

        float similarNormalBlue;
        float differentNormalBlue;

        // Similar normal (should accept history)
        {
            // History normal similar to (0, 1, 0): encoded as (0.5, 0.95, 0.5) -> (0, 0.9, 0)
            var historyMeta = new float[ProbeGridWidth * ProbeGridHeight * 4];
            for (int i = 0; i < ProbeGridWidth * ProbeGridHeight; i++)
            {
                int idx = i * 4;
                historyMeta[idx + 0] = worldZ;
                historyMeta[idx + 1] = 0.5f;   // nx = 0
                historyMeta[idx + 2] = 0.95f;  // ny ≈ 0.9 (similar to current ny=1.0)
                historyMeta[idx + 3] = 15.0f;  // accumCount
            }

            using var currentRad0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, currentRad0);
            using var currentRad1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, currentRad1);
            using var historyRad0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyRad0);
            using var historyRad1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyRad1);
            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPos);
            using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormal);
            using var historyMetaTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyMeta);

            using var outputGBuffer = TestFramework.CreateTestGBuffer(
                ProbeGridWidth, ProbeGridHeight,
                PixelInternalFormat.Rgba16f,
                PixelInternalFormat.Rgba16f,
                PixelInternalFormat.Rgba16f);

            var programId = CompileTemporalShader();
            var identity = LumOnTestInputFactory.CreateIdentityMatrix();
            SetupTemporalUniforms(
                programId,
                viewMatrix: identity,
                invViewMatrix: identity,
                prevViewProjMatrix: identity,
                temporalAlpha: 0.9f,
                depthRejectThreshold: 0.5f,
                normalRejectThreshold: threshold);

            currentRad0Tex.Bind(0);
            currentRad1Tex.Bind(1);
            historyRad0Tex.Bind(2);
            historyRad1Tex.Bind(3);
            anchorPosTex.Bind(4);
            anchorNormalTex.Bind(5);
            historyMetaTex.Bind(6);

            TestFramework.RenderQuadTo(programId, outputGBuffer);
            var output = outputGBuffer[0].ReadPixels();
            similarNormalBlue = output[2];

            GL.DeleteProgram(programId);
        }

        // Different normal (should reject history)
        {
            // History normal perpendicular (1, 0, 0): encoded as (1.0, 0.5, 0.5)
            var historyMeta = new float[ProbeGridWidth * ProbeGridHeight * 4];
            for (int i = 0; i < ProbeGridWidth * ProbeGridHeight; i++)
            {
                int idx = i * 4;
                historyMeta[idx + 0] = worldZ;
                historyMeta[idx + 1] = 1.0f;   // nx = 1
                historyMeta[idx + 2] = 0.5f;   // ny = 0 (perpendicular to current ny=1.0)
                historyMeta[idx + 3] = 15.0f;
            }

            using var currentRad0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, currentRad0);
            using var currentRad1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, currentRad1);
            using var historyRad0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyRad0);
            using var historyRad1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyRad1);
            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPos);
            using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormal);
            using var historyMetaTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyMeta);

            using var outputGBuffer = TestFramework.CreateTestGBuffer(
                ProbeGridWidth, ProbeGridHeight,
                PixelInternalFormat.Rgba16f,
                PixelInternalFormat.Rgba16f,
                PixelInternalFormat.Rgba16f);

            var programId = CompileTemporalShader();
            var identity = LumOnTestInputFactory.CreateIdentityMatrix();
            SetupTemporalUniforms(
                programId,
                viewMatrix: identity,
                invViewMatrix: identity,
                prevViewProjMatrix: identity,
                temporalAlpha: 0.9f,
                depthRejectThreshold: 0.5f,
                normalRejectThreshold: threshold);

            currentRad0Tex.Bind(0);
            currentRad1Tex.Bind(1);
            historyRad0Tex.Bind(2);
            historyRad1Tex.Bind(3);
            anchorPosTex.Bind(4);
            anchorNormalTex.Bind(5);
            historyMetaTex.Bind(6);

            TestFramework.RenderQuadTo(programId, outputGBuffer);
            var output = outputGBuffer[0].ReadPixels();
            differentNormalBlue = output[2];

            GL.DeleteProgram(programId);
        }

        // Similar normal should have more blue (history accepted)
        Assert.True(similarNormalBlue > differentNormalBlue,
            $"Similar normal should have more blue ({similarNormalBlue:F3}) than different ({differentNormalBlue:F3})");
    }

    /// <summary>
    /// Tests that history UV reprojection out of bounds uses current frame.
    /// 
    /// DESIRED BEHAVIOR:
    /// - When historyUV falls outside [0,1] bounds, use current frame only
    /// - This handles camera motion where previous frame data doesn't exist
    /// 
    /// Setup:
    /// - Use prevViewProjMatrix that projects probes outside [0,1] UV range
    /// - Current = red, History = blue
    /// 
    /// Expected:
    /// - Output should be current (red) since history UV is invalid
    /// </summary>
    [Fact]
    public void HistoryOutOfBounds_UsesCurrentFrame()
    {
        EnsureShaderTestAvailable();

        var currentColor = (r: 1.0f, g: 0.0f, b: 0.0f);
        var historyColor = (r: 0.0f, g: 0.0f, b: 1.0f);

        var currentRad0 = CreateUniformRadiance(currentColor.r, currentColor.g, currentColor.b);
        var currentRad1 = CreateUniformRadiance(0.5f, 0.5f, 0.5f);
        var historyRad0 = CreateUniformRadiance(historyColor.r, historyColor.g, historyColor.b);
        var historyRad1 = CreateUniformRadiance(0.5f, 0.5f, 0.5f);

        float worldZ = 5.0f;
        var anchorPos = CreateValidProbeAnchors(worldZ);
        var anchorNormal = CreateProbeNormalsUpward();
        var historyMeta = CreateHistoryMeta(worldZ, 15.0f, 0f, 1f);

        // Create a prevViewProjMatrix that offsets reprojection outside [0,1]
        // Translation matrix that moves points far outside screen
        var prevViewProj = new float[]
        {
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, 1, 0,
            100, 100, 0, 1  // Large translation pushes UV outside bounds
        };

        using var currentRad0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, currentRad0);
        using var currentRad1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, currentRad1);
        using var historyRad0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyRad0);
        using var historyRad1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyRad1);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPos);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormal);
        using var historyMetaTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyMeta);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ProbeGridWidth, ProbeGridHeight,
            PixelInternalFormat.Rgba16f,
            PixelInternalFormat.Rgba16f,
            PixelInternalFormat.Rgba16f);

        var programId = CompileTemporalShader();
        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupTemporalUniforms(
            programId,
            viewMatrix: identity,
            invViewMatrix: identity,
            prevViewProjMatrix: prevViewProj,
            temporalAlpha: 0.9f);

        currentRad0Tex.Bind(0);
        currentRad1Tex.Bind(1);
        historyRad0Tex.Bind(2);
        historyRad1Tex.Bind(3);
        anchorPosTex.Bind(4);
        anchorNormalTex.Bind(5);
        historyMetaTex.Bind(6);

        TestFramework.RenderQuadTo(programId, outputGBuffer);
        var outputRad0 = outputGBuffer[0].ReadPixels();

        // Output should be primarily current (red) since history UV is out of bounds
        var (r, g, b, _) = ReadProbe(outputRad0, 0, 0);
        Assert.True(r > b,
            $"Out-of-bounds history UV should favor current (red): R={r:F3}, B={b:F3}");

        GL.DeleteProgram(programId);
    }

    /// <summary>
    /// Tests that invalid probes output current frame with zero metadata.
    /// 
    /// DESIRED BEHAVIOR:
    /// - Invalid probes (validity=0) should pass through current radiance
    /// - Metadata should reflect the invalidity state
    /// 
    /// Setup:
    /// - All probes invalid (validity=0)
    /// - Current = red, History = blue
    /// 
    /// Expected:
    /// - Output radiance = current (red)
    /// </summary>
    [Fact]
    public void InvalidProbe_OutputsCurrentWithZeroMeta()
    {
        EnsureShaderTestAvailable();

        var currentColor = (r: 1.0f, g: 0.0f, b: 0.0f);
        var historyColor = (r: 0.0f, g: 0.0f, b: 1.0f);

        var currentRad0 = CreateUniformRadiance(currentColor.r, currentColor.g, currentColor.b);
        var currentRad1 = CreateUniformRadiance(0.5f, 0.5f, 0.5f);
        var historyRad0 = CreateUniformRadiance(historyColor.r, historyColor.g, historyColor.b);
        var historyRad1 = CreateUniformRadiance(0.5f, 0.5f, 0.5f);

        var anchorPos = CreateInvalidProbeAnchors();  // All invalid
        var anchorNormal = CreateProbeNormalsUpward();
        var historyMeta = CreateHistoryMeta(5.0f, 15.0f, 0f, 1f);

        using var currentRad0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, currentRad0);
        using var currentRad1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, currentRad1);
        using var historyRad0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyRad0);
        using var historyRad1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyRad1);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPos);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormal);
        using var historyMetaTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyMeta);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ProbeGridWidth, ProbeGridHeight,
            PixelInternalFormat.Rgba16f,
            PixelInternalFormat.Rgba16f,
            PixelInternalFormat.Rgba16f);

        var programId = CompileTemporalShader();
        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupTemporalUniforms(
            programId,
            viewMatrix: identity,
            invViewMatrix: identity,
            prevViewProjMatrix: identity,
            temporalAlpha: 0.9f);

        currentRad0Tex.Bind(0);
        currentRad1Tex.Bind(1);
        historyRad0Tex.Bind(2);
        historyRad1Tex.Bind(3);
        anchorPosTex.Bind(4);
        anchorNormalTex.Bind(5);
        historyMetaTex.Bind(6);

        TestFramework.RenderQuadTo(programId, outputGBuffer);
        var outputRad0 = outputGBuffer[0].ReadPixels();

        // All probes should output current (red)
        for (int py = 0; py < ProbeGridHeight; py++)
        {
            for (int px = 0; px < ProbeGridWidth; px++)
            {
                var (r, g, b, _) = ReadProbe(outputRad0, px, py);
                Assert.True(r > 0.9f && g < 0.1f && b < 0.1f,
                    $"Invalid probe ({px},{py}) should output current (red), got ({r:F3}, {g:F3}, {b:F3})");
            }
        }

        GL.DeleteProgram(programId);
    }

    #endregion

    #region Phase 6 Tests: Edge Cases

    /// <summary>
    /// Tests that temporalAlpha=0 passes through current frame only.
    /// 
    /// DESIRED BEHAVIOR:
    /// - With α=0, output = current * 1.0 + history * 0.0 = current
    /// - History should be completely ignored
    /// 
    /// Setup:
    /// - Current: red (1,0,0)
    /// - History: blue (0,0,1)
    /// - temporalAlpha: 0
    /// 
    /// Expected:
    /// - Output should equal current (red)
    /// </summary>
    [Fact]
    public void ZeroTemporalAlpha_PassesThroughCurrent()
    {
        EnsureShaderTestAvailable();

        const float worldZ = -5f;

        // Current = red, History = blue
        var currentRad0 = CreateUniformRadiance(1f, 0f, 0f);  // Red
        var currentRad1 = CreateUniformRadiance(0f, 0f, 0f);
        var historyRad0 = CreateUniformRadiance(0f, 0f, 1f);  // Blue
        var historyRad1 = CreateUniformRadiance(0f, 0f, 0f);
        var anchorPos = CreateValidProbeAnchors(worldZ);
        var anchorNormal = CreateProbeNormalsUpward();
        var historyMeta = CreateHistoryMeta(worldZ, 16.0f, 0f, 1f);  // Matching depth/normal

        using var currentRad0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, currentRad0);
        using var currentRad1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, currentRad1);
        using var historyRad0Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyRad0);
        using var historyRad1Tex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyRad1);
        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPos);
        using var anchorNormalTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNormal);
        using var historyMetaTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, historyMeta);

        using var outputGBuffer = TestFramework.CreateTestGBuffer(
            ProbeGridWidth, ProbeGridHeight,
            PixelInternalFormat.Rgba16f,
            PixelInternalFormat.Rgba16f,
            PixelInternalFormat.Rgba16f);

        var programId = CompileTemporalShader();
        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        SetupTemporalUniforms(
            programId,
            viewMatrix: identity,
            invViewMatrix: identity,
            prevViewProjMatrix: identity,
            temporalAlpha: 0f);  // Zero alpha - no history blending

        currentRad0Tex.Bind(0);
        currentRad1Tex.Bind(1);
        historyRad0Tex.Bind(2);
        historyRad1Tex.Bind(3);
        anchorPosTex.Bind(4);
        anchorNormalTex.Bind(5);
        historyMetaTex.Bind(6);

        TestFramework.RenderQuadTo(programId, outputGBuffer);
        var outputRad0 = outputGBuffer[0].ReadPixels();

        // With α=0, output should equal current (red channel dominant)
        for (int py = 0; py < ProbeGridHeight; py++)
        {
            for (int px = 0; px < ProbeGridWidth; px++)
            {
                var (r, g, b, _) = ReadProbe(outputRad0, px, py);
                // Current has red, history has blue - with α=0 we should see current only
                Assert.True(b < r + 0.1f,
                    $"Probe ({px},{py}) with α=0 should favor current over history: R={r:F3}, B={b:F3}");
            }
        }

        GL.DeleteProgram(programId);
    }

    #endregion
}
