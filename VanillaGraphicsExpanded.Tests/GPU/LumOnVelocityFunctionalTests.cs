using VanillaGraphicsExpanded.LumOn;
using System;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>
/// Functional tests for the LumOn Velocity shader pass (Phase 14).
///
/// These tests render <c>lumon_velocity</c> into a tiny offscreen target and verify:
/// - Static camera produces ~zero velocity
/// - Pure translation produces expected sign/direction
/// - Pure rotation produces a larger magnitude at screen edges
///
/// Encoding (RGBA32F):
/// - RG: velocityUv = currUv - prevUv
/// - A : uintBitsToFloat(flags)
/// </summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public class LumOnVelocityFunctionalTests : LumOnShaderFunctionalTestBase
{
    private const uint FlagValid = 1u << 0;

    public LumOnVelocityFunctionalTests(HeadlessGLFixture fixture) : base(fixture) { }

    private LumOnVelocityShaderProgram CompileVelocityShader() => Programs.Create<LumOnVelocityShaderProgram>();

    private static float PackFlags(uint flags) => BitConverter.UInt32BitsToSingle(flags);

    private void SetupVelocityUniforms(
        LumOnVelocityShaderProgram programId,
        float[] invCurrViewProj,
        float[] prevViewProj,
        int historyValid,
        int depthUnit = 0)
    {
        using var use = programId.UseScope();
        UpdateAndBindLumOnFrameUbo(
            programId,
            invCurrViewProjMatrix: invCurrViewProj,
            prevViewProjMatrix: prevViewProj,
            historyValid: historyValid);

    }

    private static Vector4 MulMat4Vec4(float[] m, Vector4 v)
    {
        // Column-major 4x4: m[col*4 + row]
        float x = m[0] * v.X + m[4] * v.Y + m[8] * v.Z + m[12] * v.W;
        float y = m[1] * v.X + m[5] * v.Y + m[9] * v.Z + m[13] * v.W;
        float z = m[2] * v.X + m[6] * v.Y + m[10] * v.Z + m[14] * v.W;
        float w = m[3] * v.X + m[7] * v.Y + m[11] * v.Z + m[15] * v.W;
        return new Vector4(x, y, z, w);
    }

    private static Vector2 ComputeExpectedVelocityUv(Vector2 currUv, float depthRaw, float[] invCurrViewProj, float[] prevViewProj)
    {
        var currClip = new Vector4(
            currUv.X * 2f - 1f,
            currUv.Y * 2f - 1f,
            depthRaw * 2f - 1f,
            1f);

        var worldPosH = MulMat4Vec4(invCurrViewProj, currClip);
        var worldPos = new Vector3(worldPosH.X, worldPosH.Y, worldPosH.Z) / worldPosH.W;

        var prevClip = MulMat4Vec4(prevViewProj, new Vector4(worldPos, 1f));
        var prevNdc = new Vector2(prevClip.X / prevClip.W, prevClip.Y / prevClip.W);
        var prevUv = prevNdc * 0.5f + new Vector2(0.5f, 0.5f);

        return currUv - prevUv;
    }

    private static float[] CreateRotationZ(float radians)
    {
        float c = MathF.Cos(radians);
        float s = MathF.Sin(radians);

        // Column-major
        return
        [
            c, s, 0f, 0f,   // col0
            -s, c, 0f, 0f,  // col1
            0f, 0f, 1f, 0f, // col2
            0f, 0f, 0f, 1f  // col3
        ];
    }

    #region Render origin regression
    /// <summary>The production shader projects fixed world points through paired frame origins including rotation and view bob.</summary>
    [Theory]
    [InlineData(16777216.25, true)]
    [InlineData(-16777216.25, true)]
    [InlineData(16777216.25, false)]
    public void Velocity_RenderOriginMovementPreservesWorldReprojection(double origin, bool historyValid)
    {
        EnsureShaderTestAvailable();
        var program = CompileVelocityShader();
        using var use = program.UseScope();
        using var depth = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f,
            CreateUniformDepthData(ScreenWidth, ScreenHeight, .5f));
        using var target = TestFramework.CreateTestGBuffer(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba32f);
        float[] previous = CreateRotationZ(.1f);
        previous[12] = .015625f;
        previous[13] = -.03125f;
        float[] current = LumOnTestInputFactory.CreateIdentityMatrix();
        var temporal = new LumOnTemporalReprojection();
        temporal.Capture(previous, origin, 32, -origin);
        temporal.Commit();
        temporal.Capture(current, origin + .0625, 32.125, -origin - .03125);
        SetupVelocityUniforms(program, current, temporal.PreviousViewProjection, historyValid ? 1 : 0);
        program.PrimaryDepth = depth.TextureId;
        TestFramework.RenderQuadTo(program, target);
        var pixels = ReadPixelsFloat(target);
        for (int y = 1; y < ScreenHeight - 1; y++)
        for (int x = 1; x < ScreenWidth - 1; x++)
        {
            Vector2 uv = new((x + .5f) / ScreenWidth, (y + .5f) / ScreenHeight);
            // Convert current-relative coordinates to the old frame before applying its raw matrix.
            Vector4 oldRelative = new(uv.X * 2 - 1 + .0625f, uv.Y * 2 - 1 + .125f, -.03125f, 1);
            Vector4 clip = MulMat4Vec4(previous, oldRelative);
            Vector2 expected = historyValid ? uv - new Vector2(clip.X / clip.W, clip.Y / clip.W) * .5f - new Vector2(.5f) : Vector2.Zero;
            int index = (y * ScreenWidth + x) * 4;
            Assert.InRange(pixels[index], expected.X - TestEpsilon, expected.X + TestEpsilon);
            Assert.InRange(pixels[index + 1], expected.Y - TestEpsilon, expected.Y + TestEpsilon);
            Assert.Equal(historyValid ? 1u : 2u, BitConverter.SingleToUInt32Bits(pixels[index + 3]));
        }
    }
    #endregion

    [Fact]
    public void Velocity_StaticCamera_IsZero()
    {
        EnsureShaderTestAvailable();

        var programId = CompileVelocityShader();

        using var programUse = programId.UseScope();

        // Depth that is not sky and not zero.
        var depthData = CreateUniformDepthData(ScreenWidth, ScreenHeight, depth: 0.5f);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);

        using var outRt = TestFramework.CreateTestGBuffer(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba32f);

        float[] invCurr = LumOnTestInputFactory.CreateIdentityMatrix();
        float[] prev = LumOnTestInputFactory.CreateIdentityMatrix();

        SetupVelocityUniforms(programId, invCurr, prev, historyValid: 1);

        programId.PrimaryDepth = depthTex.TextureId;
        TestFramework.RenderQuadTo(programId, outRt);

        var pixels = ReadPixelsFloat(outRt);

        for (int y = 0; y < ScreenHeight; y++)
        {
            for (int x = 0; x < ScreenWidth; x++)
            {
                int idx = (y * ScreenWidth + x) * 4;
                float vx = pixels[idx + 0];
                float vy = pixels[idx + 1];
                float packed = pixels[idx + 3];

                Assert.InRange(vx, -TestEpsilon, TestEpsilon);
                Assert.InRange(vy, -TestEpsilon, TestEpsilon);

                uint flags = BitConverter.SingleToUInt32Bits(packed);
                Assert.True((flags & FlagValid) != 0u);
            }
        }
    }

    [Fact]
    public void Velocity_PureTranslation_ProducesExpectedDirection()
    {
        EnsureShaderTestAvailable();

        var programId = CompileVelocityShader();

        using var programUse = programId.UseScope();

        var depthData = CreateUniformDepthData(ScreenWidth, ScreenHeight, depth: 0.5f);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);

        using var outRt = TestFramework.CreateTestGBuffer(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba32f);

        // With invCurr = I and prevViewProj = translate(+tx,0,0) in clip space,
        // prevUv.x = currUv.x + tx*0.5 => velocityUv.x = -tx*0.5.
        const float tx = 0.2f;
        float expectedVx = -tx * 0.5f;

        float[] invCurr = LumOnTestInputFactory.CreateIdentityMatrix();
        float[] prev = LumOnTestInputFactory.CreateTranslationMatrix(tx, 0f, 0f);

        SetupVelocityUniforms(programId, invCurr, prev, historyValid: 1);

        programId.PrimaryDepth = depthTex.TextureId;
        TestFramework.RenderQuadTo(programId, outRt);

        var pixels = ReadPixelsFloat(outRt);

        for (int y = 0; y < ScreenHeight; y++)
        {
            for (int x = 0; x < ScreenWidth; x++)
            {
                int idx = (y * ScreenWidth + x) * 4;
                float vx = pixels[idx + 0];
                float vy = pixels[idx + 1];
                float packed = pixels[idx + 3];

                Assert.InRange(vx, expectedVx - TestEpsilon, expectedVx + TestEpsilon);
                Assert.InRange(vy, -TestEpsilon, TestEpsilon);

                uint flags = BitConverter.SingleToUInt32Bits(packed);
                Assert.True((flags & FlagValid) != 0u);
            }
        }
    }

    [Fact]
    public void Velocity_PureRotation_ProducesExpectedPattern()
    {
        EnsureShaderTestAvailable();

        var programId = CompileVelocityShader();

        using var programUse = programId.UseScope();

        var depthData = CreateUniformDepthData(ScreenWidth, ScreenHeight, depth: 0.5f);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);

        using var outRt = TestFramework.CreateTestGBuffer(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba32f);

        float[] invCurr = LumOnTestInputFactory.CreateIdentityMatrix();
        float[] prev = CreateRotationZ(radians: 10f * MathF.PI / 180f);

        SetupVelocityUniforms(programId, invCurr, prev, historyValid: 1);

        programId.PrimaryDepth = depthTex.TextureId;
        TestFramework.RenderQuadTo(programId, outRt);

        var pixels = ReadPixelsFloat(outRt);

        // Compare center-ish pixel vs corner pixel magnitudes.
        // gl_FragCoord.xy is (x+0.5, y+0.5); currUv is divided by screenSize.
        Vector2 centerUv = new((1.5f) / ScreenWidth, (1.5f) / ScreenHeight); // pixel (1,1)
        Vector2 cornerUv = new((3.5f) / ScreenWidth, (3.5f) / ScreenHeight); // pixel (3,3)

        Vector2 expectedCenter = ComputeExpectedVelocityUv(centerUv, depthRaw: 0.5f, invCurr, prev);
        Vector2 expectedCorner = ComputeExpectedVelocityUv(cornerUv, depthRaw: 0.5f, invCurr, prev);

        float expectedCenterMag = expectedCenter.Length();
        float expectedCornerMag = expectedCorner.Length();

        Assert.True(expectedCornerMag > expectedCenterMag + 1e-4f);

        // Read back those exact pixels and compare against expected vectors.
        static (float vx, float vy, uint flags) GetPixel(float[] data, int x, int y, int w)
        {
            int idx = (y * w + x) * 4;
            float vx = data[idx + 0];
            float vy = data[idx + 1];
            uint flags = BitConverter.SingleToUInt32Bits(data[idx + 3]);
            return (vx, vy, flags);
        }

        var centerPx = GetPixel(pixels, x: 1, y: 1, w: ScreenWidth);
        var cornerPx = GetPixel(pixels, x: 3, y: 3, w: ScreenWidth);

        Assert.True((centerPx.flags & FlagValid) != 0u);
        Assert.True((cornerPx.flags & FlagValid) != 0u);

        Assert.InRange(centerPx.vx, expectedCenter.X - TestEpsilon, expectedCenter.X + TestEpsilon);
        Assert.InRange(centerPx.vy, expectedCenter.Y - TestEpsilon, expectedCenter.Y + TestEpsilon);

        Assert.InRange(cornerPx.vx, expectedCorner.X - TestEpsilon, expectedCorner.X + TestEpsilon);
        Assert.InRange(cornerPx.vy, expectedCorner.Y - TestEpsilon, expectedCorner.Y + TestEpsilon);
    }
}
