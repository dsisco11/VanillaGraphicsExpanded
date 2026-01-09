using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>
/// Functional tests for the LumOn Octahedral Temporal shader pass.
/// 
/// These tests verify that the octahedral temporal shader correctly:
/// - Blends traced texels between current and history frames
/// - Preserves non-traced texels (temporal distribution)
/// - Detects disocclusion via hit distance changes
/// - Clamps history to neighborhood bounds (prevents ghosting)
/// - Outputs zero for invalid probes
/// 
/// Test configuration:
/// - Probe grid: 2×2 probes
/// - Octahedral size: 8×8 texels per probe
/// - Atlas size: 16×16 (2×8 by 2×8)
/// </summary>
/// <remarks>
/// Key differences from SH temporal:
/// - Works per-texel in octahedral atlas (not per-probe SH coefficients)
/// - Uses hit distance for disocclusion (not depth/normal comparison)
/// - Temporal distribution: only traced texels are blended
/// - Neighborhood clamping within probe's octahedral tile
/// 
/// Temporal blend formula:
/// <code>
/// if (wasTracedThisFrame(texel)) {
///     if (historyValid) {
///         clampedHistory = clamp(history, neighborhoodMin, neighborhoodMax);
///         result = mix(current, clampedHistory, temporalAlpha);
///     } else {
///         result = current;  // Disocclusion reset
///     }
/// } else {
///     result = current;  // Preserve trace pass output (already copied history)
/// }
/// </code>
/// </remarks>
[Collection("GPU")]
[Trait("Category", "GPU")]
public class LumOnTemporalOctahedralFunctionalTests : LumOnShaderFunctionalTestBase
{
    // Default temporal parameters
    private const float DefaultTemporalAlpha = 0.9f;
    private const float DefaultHitDistanceRejectThreshold = 0.3f;  // 30% relative difference
    private const int DefaultTexelsPerFrame = 8;  // Trace 8 texels per probe per frame

    public LumOnTemporalOctahedralFunctionalTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Helper Methods

    /// <summary>
    /// Compiles and links the octahedral temporal shader.
    /// </summary>
    private int CompileOctahedralTemporalShader() => CompileShader("lumon_temporal_octahedral.vsh", "lumon_temporal_octahedral.fsh");

    /// <summary>
    /// Sets up common uniforms for the octahedral temporal shader.
    /// </summary>
    private void SetupOctahedralTemporalUniforms(
        int programId,
        int frameIndex = 0,
        int texelsPerFrame = DefaultTexelsPerFrame,
        float temporalAlpha = DefaultTemporalAlpha,
        float hitDistanceRejectThreshold = DefaultHitDistanceRejectThreshold)
    {
        GL.UseProgram(programId);

        // Probe grid uniforms
        var gridSizeLoc = GL.GetUniformLocation(programId, "probeGridSize");
        GL.Uniform2(gridSizeLoc, (float)ProbeGridWidth, (float)ProbeGridHeight);

        // Temporal distribution parameters
        var frameIndexLoc = GL.GetUniformLocation(programId, "frameIndex");
        var texelsPerFrameLoc = GL.GetUniformLocation(programId, "texelsPerFrame");
        GL.Uniform1(frameIndexLoc, frameIndex);
        GL.Uniform1(texelsPerFrameLoc, texelsPerFrame);

        // Temporal blending parameters
        var alphaLoc = GL.GetUniformLocation(programId, "temporalAlpha");
        var hitDistThreshLoc = GL.GetUniformLocation(programId, "hitDistanceRejectThreshold");
        GL.Uniform1(alphaLoc, temporalAlpha);
        GL.Uniform1(hitDistThreshLoc, hitDistanceRejectThreshold);

        // Texture sampler uniforms
        var currentLoc = GL.GetUniformLocation(programId, "octahedralCurrent");
        var historyLoc = GL.GetUniformLocation(programId, "octahedralHistory");
        var anchorPosLoc = GL.GetUniformLocation(programId, "probeAnchorPosition");
        GL.Uniform1(currentLoc, 0);
        GL.Uniform1(historyLoc, 1);
        GL.Uniform1(anchorPosLoc, 2);

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
                data[idx + 0] = (px + 0.5f) * 2f - 2f;  // X position
                data[idx + 1] = (py + 0.5f) * 2f - 2f;  // Y position
                data[idx + 2] = 0f;                      // Z position
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
    /// Creates a uniform color atlas buffer for all probes.
    /// </summary>
    private static float[] CreateUniformAtlas(float r, float g, float b, float hitDistanceEncoded)
    {
        var data = new float[AtlasWidth * AtlasHeight * 4];
        for (int i = 0; i < AtlasWidth * AtlasHeight; i++)
        {
            int idx = i * 4;
            data[idx + 0] = r;
            data[idx + 1] = g;
            data[idx + 2] = b;
            data[idx + 3] = hitDistanceEncoded;
        }
        return data;
    }

    /// <summary>
    /// Creates an atlas buffer with different colors for current vs history.
    /// Current = red, History = blue allows easy visual distinction.
    /// </summary>
    private static float[] CreateCurrentAtlas(float hitDistanceEncoded = 1.0f)
    {
        return CreateUniformAtlas(1.0f, 0.0f, 0.0f, hitDistanceEncoded);  // Red
    }

    /// <summary>
    /// Creates history atlas with distinct color.
    /// </summary>
    private static float[] CreateHistoryAtlas(float hitDistanceEncoded = 1.0f)
    {
        return CreateUniformAtlas(0.0f, 0.0f, 1.0f, hitDistanceEncoded);  // Blue
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

    /// <summary>
    /// Determines if a texel was traced this frame (matches shader logic).
    /// </summary>
    private static bool WasTracedThisFrame(int octX, int octY, int probeIndex, int frameIndex, int texelsPerFrame)
    {
        int texelIndex = octY * OctahedralSize + octX;
        int numBatches = (OctahedralSize * OctahedralSize) / texelsPerFrame;
        int batch = texelIndex / texelsPerFrame;
        int jitteredFrame = (frameIndex + probeIndex) % numBatches;
        return batch == jitteredFrame;
    }

    #endregion

    #region Test: InvalidProbe_OutputsZero

    /// <summary>
    /// Tests that invalid probes produce zero output in their atlas region.
    /// 
    /// DESIRED BEHAVIOR:
    /// - Invalid probes (validity=0) should output zero radiance
    /// - This prevents invalid probes from contributing to lighting
    /// 
    /// Setup:
    /// - All probes invalid
    /// - Current atlas: red (1,0,0)
    /// - History atlas: blue (0,0,1)
    /// 
    /// Expected:
    /// - All 256 texels output (0,0,0,0)
    /// </summary>
    [Fact]
    public void InvalidProbe_OutputsZero()
    {
        EnsureShaderTestAvailable();

        var anchorPos = CreateInvalidProbeAnchors();
        var currentAtlas = CreateCurrentAtlas();
        var historyAtlas = CreateHistoryAtlas();

        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPos);
        using var currentAtlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, currentAtlas);
        using var historyAtlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, historyAtlas);

        using var outputAtlas = TestFramework.CreateTestGBuffer(
            AtlasWidth, AtlasHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileOctahedralTemporalShader();
        SetupOctahedralTemporalUniforms(programId);

        currentAtlasTex.Bind(0);
        historyAtlasTex.Bind(1);
        anchorPosTex.Bind(2);

        TestFramework.RenderQuadTo(programId, outputAtlas);
        var outputData = outputAtlas[0].ReadPixels();

        // Verify all texels are zero
        int zeroCount = 0;
        for (int y = 0; y < AtlasHeight; y++)
        {
            for (int x = 0; x < AtlasWidth; x++)
            {
                var (r, g, b, a) = ReadAtlasTexel(outputData, x, y);
                if (MathF.Abs(r) < TestEpsilon &&
                    MathF.Abs(g) < TestEpsilon &&
                    MathF.Abs(b) < TestEpsilon &&
                    MathF.Abs(a) < TestEpsilon)
                {
                    zeroCount++;
                }
            }
        }

        Assert.True(zeroCount == AtlasWidth * AtlasHeight,
            $"Expected all {AtlasWidth * AtlasHeight} texels to be zero, got {zeroCount}");

        GL.DeleteProgram(programId);
    }

    #endregion

    #region Test: NonTracedTexels_PreserveCurrentOutput

    /// <summary>
    /// Tests that non-traced texels preserve the current frame output unchanged.
    /// 
    /// DESIRED BEHAVIOR:
    /// - Texels NOT traced this frame should pass through current unchanged
    /// - The trace pass already copied history for non-traced texels
    /// - Temporal pass should not modify these texels
    /// 
    /// Setup:
    /// - frameIndex=0, texelsPerFrame=8
    /// - Probe 0 traces batch 0 (texels 0-7)
    /// - Current atlas: red
    /// - History atlas: blue
    /// 
    /// Expected:
    /// - Non-traced texels (8-63 for probe 0) = current (red)
    /// </summary>
    [Fact]
    public void NonTracedTexels_PreserveCurrentOutput()
    {
        EnsureShaderTestAvailable();

        var anchorPos = CreateValidProbeAnchors();
        var currentAtlas = CreateCurrentAtlas(EncodeHitDistance(10f));
        var historyAtlas = CreateHistoryAtlas(EncodeHitDistance(10f));

        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPos);
        using var currentAtlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, currentAtlas);
        using var historyAtlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, historyAtlas);

        using var outputAtlas = TestFramework.CreateTestGBuffer(
            AtlasWidth, AtlasHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileOctahedralTemporalShader();
        SetupOctahedralTemporalUniforms(
            programId,
            frameIndex: 0,
            texelsPerFrame: 8);

        currentAtlasTex.Bind(0);
        historyAtlasTex.Bind(1);
        anchorPosTex.Bind(2);

        TestFramework.RenderQuadTo(programId, outputAtlas);
        var outputData = outputAtlas[0].ReadPixels();

        // Check probe (0,0) - probeIndex=0
        int probeIndex = 0;
        int probeX = 0, probeY = 0;
        
        int nonTracedPreserved = 0;
        int nonTracedTotal = 0;

        for (int octY = 0; octY < OctahedralSize; octY++)
        {
            for (int octX = 0; octX < OctahedralSize; octX++)
            {
                bool wasTraced = WasTracedThisFrame(octX, octY, probeIndex, 0, 8);
                
                if (!wasTraced)
                {
                    nonTracedTotal++;
                    var (x, y) = GetAtlasCoord(probeX, probeY, octX, octY);
                    var (r, g, b, _) = ReadAtlasTexel(outputData, x, y);

                    // Should be current (red)
                    if (r > 0.9f && g < 0.1f && b < 0.1f)
                    {
                        nonTracedPreserved++;
                    }
                }
            }
        }

        // With texelsPerFrame=8 and 64 total texels, 56 should be non-traced
        Assert.True(nonTracedTotal == 56,
            $"Expected 56 non-traced texels, got {nonTracedTotal}");
        Assert.True(nonTracedPreserved == nonTracedTotal,
            $"Expected all {nonTracedTotal} non-traced texels to be preserved, got {nonTracedPreserved}");

        GL.DeleteProgram(programId);
    }

    #endregion

    #region Test: TracedTexels_BlendWithHistory

    /// <summary>
    /// Tests that traced texels are blended with valid history.
    /// 
    /// DESIRED BEHAVIOR:
    /// - Texels traced this frame should blend current and history
    /// - Formula: result = mix(current, clampedHistory, temporalAlpha)
    /// - With α=0.5: result = 0.5*current + 0.5*history
    /// 
    /// Setup:
    /// - frameIndex=0, texelsPerFrame=64 (trace all)
    /// - Current atlas: red (1,0,0)
    /// - History atlas: blue (0,0,1)
    /// - temporalAlpha=0.5
    /// - Same hit distances (no disocclusion)
    /// 
    /// Expected:
    /// - All texels = (0.5, 0, 0.5) magenta (blended red+blue)
    /// </summary>
    [Fact]
    public void TracedTexels_BlendWithHistory()
    {
        EnsureShaderTestAvailable();

        float hitDist = 10f;
        var anchorPos = CreateValidProbeAnchors();
        var currentAtlas = CreateCurrentAtlas(EncodeHitDistance(hitDist));
        var historyAtlas = CreateHistoryAtlas(EncodeHitDistance(hitDist));

        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPos);
        using var currentAtlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, currentAtlas);
        using var historyAtlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, historyAtlas);

        using var outputAtlas = TestFramework.CreateTestGBuffer(
            AtlasWidth, AtlasHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileOctahedralTemporalShader();
        SetupOctahedralTemporalUniforms(
            programId,
            frameIndex: 0,
            texelsPerFrame: 64,  // Trace all texels
            temporalAlpha: 0.5f);

        currentAtlasTex.Bind(0);
        historyAtlasTex.Bind(1);
        anchorPosTex.Bind(2);

        TestFramework.RenderQuadTo(programId, outputAtlas);
        var outputData = outputAtlas[0].ReadPixels();

        // Check a sample of texels for blended color
        int blendedCount = 0;
        int sampleCount = 0;

        for (int probeY = 0; probeY < ProbeGridHeight; probeY++)
        {
            for (int probeX = 0; probeX < ProbeGridWidth; probeX++)
            {
                // Sample center texel of each probe
                var (x, y) = GetAtlasCoord(probeX, probeY, 4, 4);
                var (r, g, b, _) = ReadAtlasTexel(outputData, x, y);
                sampleCount++;

                // With α=0.5, current=red, history=blue:
                // result = mix(red, blue, 0.5) = (0.5, 0, 0.5)
                // Allow tolerance for neighborhood clamping effects
                if (r > 0.3f && r < 0.7f && g < 0.2f && b > 0.3f && b < 0.7f)
                {
                    blendedCount++;
                }
            }
        }

        Assert.True(blendedCount == sampleCount,
            $"Expected all {sampleCount} sampled texels to be blended, got {blendedCount}");

        GL.DeleteProgram(programId);
    }

    #endregion

    #region Test: Disocclusion_ResetsToCurrentFrame

    /// <summary>
    /// Tests that texels with significant hit distance change reset to current frame.
    /// 
    /// DESIRED BEHAVIOR:
    /// - When hit distance changes more than threshold, history is rejected
    /// - Output = current frame only (no blending)
    /// 
    /// Setup:
    /// - Current hit distance: 5.0
    /// - History hit distance: 20.0 (300% difference, > 30% threshold)
    /// - Current atlas: red
    /// - History atlas: blue
    /// 
    /// Expected:
    /// - Output = red (current) due to disocclusion
    /// </summary>
    [Fact]
    public void Disocclusion_ResetsToCurrentFrame()
    {
        EnsureShaderTestAvailable();

        float currentHitDist = 5f;
        float historyHitDist = 20f;  // 300% different

        var anchorPos = CreateValidProbeAnchors();
        var currentAtlas = CreateCurrentAtlas(EncodeHitDistance(currentHitDist));
        var historyAtlas = CreateHistoryAtlas(EncodeHitDistance(historyHitDist));

        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPos);
        using var currentAtlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, currentAtlas);
        using var historyAtlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, historyAtlas);

        using var outputAtlas = TestFramework.CreateTestGBuffer(
            AtlasWidth, AtlasHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileOctahedralTemporalShader();
        SetupOctahedralTemporalUniforms(
            programId,
            frameIndex: 0,
            texelsPerFrame: 64,
            temporalAlpha: 0.9f,
            hitDistanceRejectThreshold: 0.3f);  // 30% threshold

        currentAtlasTex.Bind(0);
        historyAtlasTex.Bind(1);
        anchorPosTex.Bind(2);

        TestFramework.RenderQuadTo(programId, outputAtlas);
        var outputData = outputAtlas[0].ReadPixels();

        // All texels should be current (red) due to disocclusion
        int currentCount = 0;
        int totalTexels = AtlasWidth * AtlasHeight;

        for (int y = 0; y < AtlasHeight; y++)
        {
            for (int x = 0; x < AtlasWidth; x++)
            {
                var (r, g, b, _) = ReadAtlasTexel(outputData, x, y);
                
                // Should be approximately red (current)
                if (r > 0.9f && g < 0.1f && b < 0.1f)
                {
                    currentCount++;
                }
            }
        }

        Assert.True(currentCount == totalTexels,
            $"Expected all {totalTexels} texels to be current (disoccluded), got {currentCount}");

        GL.DeleteProgram(programId);
    }

    #endregion

    #region Test: TemporalAlpha_ControlsBlendStrength

    /// <summary>
    /// Tests that temporalAlpha parameter controls the blend strength.
    /// 
    /// DESIRED BEHAVIOR:
    /// - Higher alpha = more history contribution
    /// - α=0.9: result = 0.1*current + 0.9*history (mostly history)
    /// - α=0.1: result = 0.9*current + 0.1*history (mostly current)
    /// 
    /// Setup:
    /// - Run twice with α=0.9 and α=0.1
    /// - Current: red (1,0,0)
    /// - History: blue (0,0,1)
    /// 
    /// Expected:
    /// - α=0.9: more blue (history)
    /// - α=0.1: more red (current)
    /// </summary>
    [Fact]
    public void TemporalAlpha_ControlsBlendStrength()
    {
        EnsureShaderTestAvailable();

        float hitDist = 10f;
        var anchorPos = CreateValidProbeAnchors();
        var currentAtlas = CreateCurrentAtlas(EncodeHitDistance(hitDist));
        var historyAtlas = CreateHistoryAtlas(EncodeHitDistance(hitDist));

        float highAlphaBlue;
        float lowAlphaBlue;

        // High alpha (more history)
        {
            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPos);
            using var currentAtlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, currentAtlas);
            using var historyAtlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, historyAtlas);

            using var outputAtlas = TestFramework.CreateTestGBuffer(
                AtlasWidth, AtlasHeight,
                PixelInternalFormat.Rgba16f);

            var programId = CompileOctahedralTemporalShader();
            SetupOctahedralTemporalUniforms(
                programId,
                frameIndex: 0,
                texelsPerFrame: 64,
                temporalAlpha: 0.9f);

            currentAtlasTex.Bind(0);
            historyAtlasTex.Bind(1);
            anchorPosTex.Bind(2);

            TestFramework.RenderQuadTo(programId, outputAtlas);
            var outputData = outputAtlas[0].ReadPixels();

            var (_, _, b, _) = ReadAtlasTexel(outputData, 4, 4);
            highAlphaBlue = b;

            GL.DeleteProgram(programId);
        }

        // Low alpha (more current)
        {
            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPos);
            using var currentAtlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, currentAtlas);
            using var historyAtlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, historyAtlas);

            using var outputAtlas = TestFramework.CreateTestGBuffer(
                AtlasWidth, AtlasHeight,
                PixelInternalFormat.Rgba16f);

            var programId = CompileOctahedralTemporalShader();
            SetupOctahedralTemporalUniforms(
                programId,
                frameIndex: 0,
                texelsPerFrame: 64,
                temporalAlpha: 0.1f);

            currentAtlasTex.Bind(0);
            historyAtlasTex.Bind(1);
            anchorPosTex.Bind(2);

            TestFramework.RenderQuadTo(programId, outputAtlas);
            var outputData = outputAtlas[0].ReadPixels();

            var (_, _, b, _) = ReadAtlasTexel(outputData, 4, 4);
            lowAlphaBlue = b;

            GL.DeleteProgram(programId);
        }

        // High alpha should have more blue (history) than low alpha
        Assert.True(highAlphaBlue > lowAlphaBlue,
            $"High alpha should have more blue ({highAlphaBlue:F3}) than low alpha ({lowAlphaBlue:F3})");
    }

    #endregion

    #region Test: FrameIndex_AffectsTracedTexels

    /// <summary>
    /// Tests that different frame indices trace different texel batches.
    /// 
    /// DESIRED BEHAVIOR:
    /// - Each frame traces a different batch of texels
    /// - With texelsPerFrame=8 and 64 total, there are 8 batches
    /// - frameIndex 0 traces batch 0, frameIndex 1 traces batch 1, etc.
    /// 
    /// Setup:
    /// - Run with frameIndex=0 and frameIndex=1
    /// - Check which texels were blended (traced) vs preserved
    /// 
    /// Expected:
    /// - Different texels are traced for different frame indices
    /// </summary>
    [Fact]
    public void FrameIndex_AffectsTracedTexels()
    {
        EnsureShaderTestAvailable();

        float hitDist = 10f;
        var anchorPos = CreateValidProbeAnchors();
        var currentAtlas = CreateCurrentAtlas(EncodeHitDistance(hitDist));
        var historyAtlas = CreateHistoryAtlas(EncodeHitDistance(hitDist));

        float[] frame0Output;
        float[] frame1Output;

        // Frame 0
        {
            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPos);
            using var currentAtlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, currentAtlas);
            using var historyAtlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, historyAtlas);

            using var outputAtlas = TestFramework.CreateTestGBuffer(
                AtlasWidth, AtlasHeight,
                PixelInternalFormat.Rgba16f);

            var programId = CompileOctahedralTemporalShader();
            SetupOctahedralTemporalUniforms(
                programId,
                frameIndex: 0,
                texelsPerFrame: 8,
                temporalAlpha: 0.5f);

            currentAtlasTex.Bind(0);
            historyAtlasTex.Bind(1);
            anchorPosTex.Bind(2);

            TestFramework.RenderQuadTo(programId, outputAtlas);
            frame0Output = outputAtlas[0].ReadPixels();

            GL.DeleteProgram(programId);
        }

        // Frame 1
        {
            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPos);
            using var currentAtlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, currentAtlas);
            using var historyAtlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, historyAtlas);

            using var outputAtlas = TestFramework.CreateTestGBuffer(
                AtlasWidth, AtlasHeight,
                PixelInternalFormat.Rgba16f);

            var programId = CompileOctahedralTemporalShader();
            SetupOctahedralTemporalUniforms(
                programId,
                frameIndex: 1,
                texelsPerFrame: 8,
                temporalAlpha: 0.5f);

            currentAtlasTex.Bind(0);
            historyAtlasTex.Bind(1);
            anchorPosTex.Bind(2);

            TestFramework.RenderQuadTo(programId, outputAtlas);
            frame1Output = outputAtlas[0].ReadPixels();

            GL.DeleteProgram(programId);
        }

        // Compare outputs - they should differ since different texels are traced
        int differingTexels = 0;
        for (int i = 0; i < frame0Output.Length; i += 4)
        {
            // Check if RGB differs (ignore alpha)
            float dr = MathF.Abs(frame0Output[i] - frame1Output[i]);
            float dg = MathF.Abs(frame0Output[i + 1] - frame1Output[i + 1]);
            float db = MathF.Abs(frame0Output[i + 2] - frame1Output[i + 2]);
            
            if (dr > 0.1f || dg > 0.1f || db > 0.1f)
            {
                differingTexels++;
            }
        }

        // Should have some differing texels (different batches traced)
        Assert.True(differingTexels > 0,
            "Different frame indices should trace different texels, resulting in different outputs");
    }

    #endregion

    #region Test: HitDistanceThreshold_ControlsDisocclusion

    /// <summary>
    /// Tests that hitDistanceRejectThreshold controls disocclusion sensitivity.
    /// 
    /// DESIRED BEHAVIOR:
    /// - Higher threshold = more tolerant of distance changes
    /// - Lower threshold = more aggressive rejection
    /// 
    /// Setup:
    /// - Current hit distance: 10
    /// - History hit distance: 15 (50% difference)
    /// - Test with threshold=0.3 (30%) and threshold=0.8 (80%)
    /// 
    /// Expected:
    /// - 30% threshold: reject history (50% > 30%)
    /// - 80% threshold: accept history (50% < 80%)
    /// </summary>
    [Fact]
    public void HitDistanceThreshold_ControlsDisocclusion()
    {
        EnsureShaderTestAvailable();

        float currentHitDist = 10f;
        float historyHitDist = 15f;  // 50% difference

        var anchorPos = CreateValidProbeAnchors();
        var currentAtlas = CreateCurrentAtlas(EncodeHitDistance(currentHitDist));
        var historyAtlas = CreateHistoryAtlas(EncodeHitDistance(historyHitDist));

        float strictThresholdBlue;
        float looseThresholdBlue;

        // Strict threshold (should reject)
        {
            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPos);
            using var currentAtlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, currentAtlas);
            using var historyAtlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, historyAtlas);

            using var outputAtlas = TestFramework.CreateTestGBuffer(
                AtlasWidth, AtlasHeight,
                PixelInternalFormat.Rgba16f);

            var programId = CompileOctahedralTemporalShader();
            SetupOctahedralTemporalUniforms(
                programId,
                frameIndex: 0,
                texelsPerFrame: 64,
                temporalAlpha: 0.9f,
                hitDistanceRejectThreshold: 0.3f);  // 30% - will reject 50% difference

            currentAtlasTex.Bind(0);
            historyAtlasTex.Bind(1);
            anchorPosTex.Bind(2);

            TestFramework.RenderQuadTo(programId, outputAtlas);
            var outputData = outputAtlas[0].ReadPixels();

            var (_, _, b, _) = ReadAtlasTexel(outputData, 4, 4);
            strictThresholdBlue = b;

            GL.DeleteProgram(programId);
        }

        // Loose threshold (should accept)
        {
            using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPos);
            using var currentAtlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, currentAtlas);
            using var historyAtlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, historyAtlas);

            using var outputAtlas = TestFramework.CreateTestGBuffer(
                AtlasWidth, AtlasHeight,
                PixelInternalFormat.Rgba16f);

            var programId = CompileOctahedralTemporalShader();
            SetupOctahedralTemporalUniforms(
                programId,
                frameIndex: 0,
                texelsPerFrame: 64,
                temporalAlpha: 0.9f,
                hitDistanceRejectThreshold: 0.8f);  // 80% - will accept 50% difference

            currentAtlasTex.Bind(0);
            historyAtlasTex.Bind(1);
            anchorPosTex.Bind(2);

            TestFramework.RenderQuadTo(programId, outputAtlas);
            var outputData = outputAtlas[0].ReadPixels();

            var (_, _, b, _) = ReadAtlasTexel(outputData, 4, 4);
            looseThresholdBlue = b;

            GL.DeleteProgram(programId);
        }

        // Strict threshold should have less blue (rejected history)
        // Loose threshold should have more blue (accepted history)
        Assert.True(looseThresholdBlue > strictThresholdBlue,
            $"Loose threshold should have more blue ({looseThresholdBlue:F3}) than strict ({strictThresholdBlue:F3})");
    }

    #endregion

    #region Test: ZeroHitDistance_TreatedAsInvalidHistory

    /// <summary>
    /// Tests that zero hit distance in history is treated as invalid.
    /// 
    /// DESIRED BEHAVIOR:
    /// - historyHitDist < 0.001 should fail validation
    /// - Output = current frame only
    /// 
    /// Setup:
    /// - Current hit distance: 10
    /// - History hit distance: 0 (invalid)
    /// 
    /// Expected:
    /// - Output = current (red), not blended
    /// </summary>
    [Fact]
    public void ZeroHitDistance_TreatedAsInvalidHistory()
    {
        EnsureShaderTestAvailable();

        var anchorPos = CreateValidProbeAnchors();
        var currentAtlas = CreateCurrentAtlas(EncodeHitDistance(10f));
        var historyAtlas = CreateHistoryAtlas(EncodeHitDistance(0f));  // Invalid

        using var anchorPosTex = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPos);
        using var currentAtlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, currentAtlas);
        using var historyAtlasTex = TestFramework.CreateTexture(AtlasWidth, AtlasHeight, PixelInternalFormat.Rgba16f, historyAtlas);

        using var outputAtlas = TestFramework.CreateTestGBuffer(
            AtlasWidth, AtlasHeight,
            PixelInternalFormat.Rgba16f);

        var programId = CompileOctahedralTemporalShader();
        SetupOctahedralTemporalUniforms(
            programId,
            frameIndex: 0,
            texelsPerFrame: 64,
            temporalAlpha: 0.9f);

        currentAtlasTex.Bind(0);
        historyAtlasTex.Bind(1);
        anchorPosTex.Bind(2);

        TestFramework.RenderQuadTo(programId, outputAtlas);
        var outputData = outputAtlas[0].ReadPixels();

        // All texels should be current (red) due to invalid history
        int currentCount = 0;
        int totalTexels = AtlasWidth * AtlasHeight;

        for (int y = 0; y < AtlasHeight; y++)
        {
            for (int x = 0; x < AtlasWidth; x++)
            {
                var (r, g, b, _) = ReadAtlasTexel(outputData, x, y);
                
                if (r > 0.9f && g < 0.1f && b < 0.1f)
                {
                    currentCount++;
                }
            }
        }

        Assert.True(currentCount == totalTexels,
            $"Expected all {totalTexels} texels to be current (invalid history), got {currentCount}");

        GL.DeleteProgram(programId);
    }

    #endregion
}
