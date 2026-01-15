using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>
/// Functional tests for Phase 14 velocity integration inside the LumOn temporal pass.
///
/// These tests ensure that when velocity reprojection is enabled:
/// - Small motion scales temporal alpha down (motionWeight)
/// - Large motion rejects history (forces current-only output)
/// </summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public class LumOnTemporalVelocityFunctionalTests : LumOnShaderFunctionalTestBase
{
    private const uint FlagValid = 1u << 0;

    public LumOnTemporalVelocityFunctionalTests(HeadlessGLFixture fixture) : base(fixture) { }

    private int CompileTemporalShader() => CompileShader("lumon_temporal.vsh", "lumon_temporal.fsh");

    /// <summary>
    /// Compiles the temporal shader with velocity reprojection enabled or disabled.
    /// </summary>
    private int CompileTemporalShaderWithVelocity(bool enabled) =>
        CompileShaderWithDefines(
            "lumon_temporal.vsh",
            "lumon_temporal.fsh",
            new Dictionary<string, string?> { ["VGE_LUMON_TEMPORAL_USE_VELOCITY_REPROJECTION"] = enabled ? "1" : "0" });

    private static float PackFlags(uint flags) => BitConverter.UInt32BitsToSingle(flags);

    /// <summary>
    /// Sets up common uniforms for the temporal shader.
    /// Note: enableReprojectionVelocity is now a compile-time define, not a uniform.
    /// Use CompileTemporalShaderWithVelocity() to select the mode.
    /// </summary>
    private void SetupTemporalVelocityUniforms(
        int programId,
        float temporalAlpha,
        float velocityRejectThreshold,
        float[]? prevViewProjMatrix = null,
        int frameIndex = 0,
        int anchorJitterEnabled = 0,
        float anchorJitterScale = 0f)
    {
        GL.UseProgram(programId);

        // Matrices (identity is fine for these tests)
        var identity = LumOnTestInputFactory.CreateIdentityMatrix();
        ShaderTestFramework.SetUniformMatrix4(GL.GetUniformLocation(programId, "viewMatrix"), identity);
        ShaderTestFramework.SetUniformMatrix4(GL.GetUniformLocation(programId, "invViewMatrix"), identity);
        ShaderTestFramework.SetUniformMatrix4(GL.GetUniformLocation(programId, "prevViewProjMatrix"), prevViewProjMatrix ?? identity);

        // Probe grid size (2x2)
        GL.Uniform2(GL.GetUniformLocation(programId, "probeGridSize"), (float)ProbeGridWidth, (float)ProbeGridHeight);

        // Z planes (not used directly in our validation setup)
        GL.Uniform1(GL.GetUniformLocation(programId, "zNear"), ZNear);
        GL.Uniform1(GL.GetUniformLocation(programId, "zFar"), ZFar);

        // Temporal params
        GL.Uniform1(GL.GetUniformLocation(programId, "temporalAlpha"), temporalAlpha);
        GL.Uniform1(GL.GetUniformLocation(programId, "depthRejectThreshold"), 0.1f);
        GL.Uniform1(GL.GetUniformLocation(programId, "normalRejectThreshold"), 0.9f);

        // Velocity reject threshold (still a uniform)
        GL.Uniform1(GL.GetUniformLocation(programId, "velocityRejectThreshold"), velocityRejectThreshold);

        // Screen mapping uniforms for probe->screen UV
        GL.Uniform2(GL.GetUniformLocation(programId, "screenSize"), (float)ScreenWidth, (float)ScreenHeight);
        GL.Uniform1(GL.GetUniformLocation(programId, "probeSpacing"), ProbeSpacing);
        GL.Uniform1(GL.GetUniformLocation(programId, "frameIndex"), frameIndex);
        GL.Uniform1(GL.GetUniformLocation(programId, "anchorJitterEnabled"), anchorJitterEnabled);
        GL.Uniform1(GL.GetUniformLocation(programId, "anchorJitterScale"), anchorJitterScale);

        // Samplers
        GL.Uniform1(GL.GetUniformLocation(programId, "radianceCurrent0"), 0);
        GL.Uniform1(GL.GetUniformLocation(programId, "radianceCurrent1"), 1);
        GL.Uniform1(GL.GetUniformLocation(programId, "radianceHistory0"), 2);
        GL.Uniform1(GL.GetUniformLocation(programId, "radianceHistory1"), 3);
        GL.Uniform1(GL.GetUniformLocation(programId, "probeAnchorPosition"), 4);
        GL.Uniform1(GL.GetUniformLocation(programId, "probeAnchorNormal"), 5);
        GL.Uniform1(GL.GetUniformLocation(programId, "historyMeta"), 6);
        GL.Uniform1(GL.GetUniformLocation(programId, "velocityTex"), 7);

        GL.UseProgram(0);
    }

    private static float[] CreateUniformRgba(int width, int height, float r, float g, float b, float a)
    {
        var data = new float[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            int idx = i * 4;
            data[idx + 0] = r;
            data[idx + 1] = g;
            data[idx + 2] = b;
            data[idx + 3] = a;
        }
        return data;
    }

    [Fact]
    public void Temporal_SmallMotion_ScalesAlpha_Down()
    {
        EnsureShaderTestAvailable();

        // Compile with velocity reprojection enabled
        int programId = CompileTemporalShaderWithVelocity(enabled: true);

        // Current radiance varies across probes so neighborhood clamping doesn't crush history.
        // Probe order in texelFetch uses probeCoord = gl_FragCoord.xy.
        // We'll set current for probe (0,0) = 0, others = 1.
        float[] current0 =
        [
            0f,0f,0f,1f,   1f,1f,1f,1f,
            1f,1f,1f,1f,   1f,1f,1f,1f
        ];
        float[] current1 = current0;

        float[] history0 = CreateUniformRgba(ProbeGridWidth, ProbeGridHeight, 1f, 1f, 1f, 1f);
        float[] history1 = history0;

        // Anchors: valid probes with posWS.z = -1 => currentDepthLin = 1.
        float[] anchorPos = CreateUniformRgba(ProbeGridWidth, ProbeGridHeight, 0f, 0f, -1f, 1f);
        // Encoded normal (0,0,1) => (0.5,0.5,1)
        float[] anchorNrm = CreateUniformRgba(ProbeGridWidth, ProbeGridHeight, 0.5f, 0.5f, 1f, 0f);

        // History meta: depth=1, normal.xy encoded = 0.5,0.5, accum=10.
        float[] meta = CreateUniformRgba(ProbeGridWidth, ProbeGridHeight, 1f, 0.5f, 0.5f, 10f);

        // Velocity texture: constant small motion with VALID flags.
        // velMag = 0.002, threshold = 0.01 => motionWeight = 0.8
        const float vx = 0.002f;
        const float vy = 0.0f;
        float packedFlags = PackFlags(FlagValid);
        float[] velData = CreateUniformRgba(ScreenWidth, ScreenHeight, vx, vy, 0f, packedFlags);

        using var texCurrent0 = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, current0);
        using var texCurrent1 = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, current1);
        using var texHistory0 = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, history0);
        using var texHistory1 = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, history1);
        using var texAnchorPos = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPos);
        using var texAnchorNrm = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNrm);
        using var texMeta = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, meta);
        using var texVel = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba32f, velData);

        using var outRt = TestFramework.CreateTestGBuffer(ProbeGridWidth, ProbeGridHeight,
            PixelInternalFormat.Rgba16f, PixelInternalFormat.Rgba16f, PixelInternalFormat.Rgba16f);

        const float temporalAlpha = 0.5f;
        const float rejectThreshold = 0.01f;

        SetupTemporalVelocityUniforms(programId, temporalAlpha, rejectThreshold);

        texCurrent0.Bind(0);
        texCurrent1.Bind(1);
        texHistory0.Bind(2);
        texHistory1.Bind(3);
        texAnchorPos.Bind(4);
        texAnchorNrm.Bind(5);
        texMeta.Bind(6);
        texVel.Bind(7);

        TestFramework.RenderQuadTo(programId, outRt);

        var pixels = ReadPixelsFloat(outRt);

        // Probe (0,0) is pixel (0,0) in the 2x2 output.
        int idx = 0 * 4;
        float outR = pixels[idx + 0];

        // Expected: output = mix(current(0), history(1), alpha)
        // alpha = temporalAlpha * motionWeight * confidence * (prevAccum/10)
        // confidence=1, prevAccum=10 => 1, motionWeight=1 - 0.002/0.01 = 0.8
        float expectedAlpha = temporalAlpha * 0.8f;
        Assert.InRange(outR, expectedAlpha - TestEpsilon, expectedAlpha + TestEpsilon);

        GL.DeleteProgram(programId);
    }

    [Fact]
    public void Temporal_LargeDelta_ForcesReset()
    {
        EnsureShaderTestAvailable();

        // Compile with velocity reprojection enabled (default)
        int programId = CompileTemporalShaderWithVelocity(enabled: true);

        // Current radiance for probe (0,0) = 0.25, others = 0.25.
        float[] current0 = CreateUniformRgba(ProbeGridWidth, ProbeGridHeight, 0.25f, 0.25f, 0.25f, 1f);
        float[] current1 = current0;

        float[] history0 = CreateUniformRgba(ProbeGridWidth, ProbeGridHeight, 1f, 1f, 1f, 1f);
        float[] history1 = history0;

        float[] anchorPos = CreateUniformRgba(ProbeGridWidth, ProbeGridHeight, 0f, 0f, -1f, 1f);
        float[] anchorNrm = CreateUniformRgba(ProbeGridWidth, ProbeGridHeight, 0.5f, 0.5f, 1f, 0f);
        float[] meta = CreateUniformRgba(ProbeGridWidth, ProbeGridHeight, 1f, 0.5f, 0.5f, 10f);

        // Velocity magnitude exceeds reject threshold => historyUV invalid => current-only output.
        const float vx = 0.02f;
        const float vy = 0.0f;
        const float rejectThreshold = 0.01f;

        float packedFlags = PackFlags(FlagValid);
        float[] velData = CreateUniformRgba(ScreenWidth, ScreenHeight, vx, vy, 0f, packedFlags);

        using var texCurrent0 = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, current0);
        using var texCurrent1 = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, current1);
        using var texHistory0 = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, history0);
        using var texHistory1 = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, history1);
        using var texAnchorPos = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPos);
        using var texAnchorNrm = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNrm);
        using var texMeta = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, meta);
        using var texVel = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba32f, velData);

        using var outRt = TestFramework.CreateTestGBuffer(ProbeGridWidth, ProbeGridHeight,
            PixelInternalFormat.Rgba16f, PixelInternalFormat.Rgba16f, PixelInternalFormat.Rgba16f);

        SetupTemporalVelocityUniforms(programId, temporalAlpha: 0.95f, velocityRejectThreshold: rejectThreshold);

        texCurrent0.Bind(0);
        texCurrent1.Bind(1);
        texHistory0.Bind(2);
        texHistory1.Bind(3);
        texAnchorPos.Bind(4);
        texAnchorNrm.Bind(5);
        texMeta.Bind(6);
        texVel.Bind(7);

        TestFramework.RenderQuadTo(programId, outRt);

        var pixels = ReadPixelsFloat(outRt);

        // Probe (0,0)
        int idx = 0 * 4;
        float outR = pixels[idx + 0];
        Assert.InRange(outR, 0.25f - TestEpsilon, 0.25f + TestEpsilon);

        GL.DeleteProgram(programId);
    }

    /// <summary>
    /// Tests that velocity-based reprojection improves history stability compared to
    /// world-space reprojection when camera motion causes out-of-bounds reprojection.
    /// Compiles two shader variants: one with velocity enabled, one disabled.
    /// </summary>
    [Fact]
    public void Temporal_MotionImprovesHistoryStability()
    {
        EnsureShaderTestAvailable();

        // Compile two shader variants
        int programIdNoVel = CompileTemporalShaderWithVelocity(enabled: false);
        int programIdWithVel = CompileTemporalShaderWithVelocity(enabled: true);

        // Current radiance varies across probes so neighborhood clamping doesn't crush history.
        float[] current0 =
        [
            0f,0f,0f,1f,   1f,1f,1f,1f,
            1f,1f,1f,1f,   1f,1f,1f,1f
        ];
        float[] current1 = current0;

        float[] history0 = CreateUniformRgba(ProbeGridWidth, ProbeGridHeight, 1f, 1f, 1f, 1f);
        float[] history1 = history0;

        float[] anchorPos = CreateUniformRgba(ProbeGridWidth, ProbeGridHeight, 0f, 0f, -1f, 1f);
        float[] anchorNrm = CreateUniformRgba(ProbeGridWidth, ProbeGridHeight, 0.5f, 0.5f, 1f, 0f);
        float[] meta = CreateUniformRgba(ProbeGridWidth, ProbeGridHeight, 1f, 0.5f, 0.5f, 10f);

        // Velocity texture: VALID and zero velocity so prevUv == currUv for the probe's screenUv.
        float packedFlags = PackFlags(FlagValid);
        float[] velData = CreateUniformRgba(ScreenWidth, ScreenHeight, 0f, 0f, 0f, packedFlags);

        using var texCurrent0 = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, current0);
        using var texCurrent1 = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, current1);
        using var texHistory0 = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, history0);
        using var texHistory1 = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, history1);
        using var texAnchorPos = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorPos);
        using var texAnchorNrm = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, anchorNrm);
        using var texMeta = TestFramework.CreateTexture(ProbeGridWidth, ProbeGridHeight, PixelInternalFormat.Rgba16f, meta);
        using var texVel = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba32f, velData);

        using var outRt = TestFramework.CreateTestGBuffer(ProbeGridWidth, ProbeGridHeight,
            PixelInternalFormat.Rgba16f, PixelInternalFormat.Rgba16f, PixelInternalFormat.Rgba16f);

        // Force the default world-space reprojection (prevViewProj) to go out-of-bounds.
        // With velocity disabled, this should reject history.
        float[] prevViewProjOob = LumOnTestInputFactory.CreateTranslationMatrix(10f, 0f, 0f);

        const float temporalAlpha = 0.5f;

        texCurrent0.Bind(0);
        texCurrent1.Bind(1);
        texHistory0.Bind(2);
        texHistory1.Bind(3);
        texAnchorPos.Bind(4);
        texAnchorNrm.Bind(5);
        texMeta.Bind(6);
        texVel.Bind(7);

        // Case A: velocity disabled -> history rejected -> output = current (0 for probe 0,0)
        SetupTemporalVelocityUniforms(programIdNoVel,
            temporalAlpha: temporalAlpha,
            velocityRejectThreshold: 0.01f,
            prevViewProjMatrix: prevViewProjOob);

        TestFramework.RenderQuadTo(programIdNoVel, outRt);
        var pixelsA = ReadPixelsFloat(outRt);
        float outA = pixelsA[0];

        // Case B: velocity enabled -> uses velocity-based prevUv (in-bounds) -> blends toward history
        SetupTemporalVelocityUniforms(programIdWithVel,
            temporalAlpha: temporalAlpha,
            velocityRejectThreshold: 0.01f,
            prevViewProjMatrix: prevViewProjOob);

        TestFramework.RenderQuadTo(programIdWithVel, outRt);
        var pixelsB = ReadPixelsFloat(outRt);
        float outB = pixelsB[0];

        Assert.InRange(outA, 0f - TestEpsilon, 0f + TestEpsilon);
        Assert.InRange(outB, temporalAlpha - TestEpsilon, temporalAlpha + TestEpsilon);

        GL.DeleteProgram(programIdNoVel);
        GL.DeleteProgram(programIdWithVel);
    }
}
