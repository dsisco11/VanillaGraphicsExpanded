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

    private int CompileVelocityShader() => CompileShader("lumon_velocity.vsh", "lumon_velocity.fsh");

    private static float PackFlags(uint flags) => BitConverter.UInt32BitsToSingle(flags);

    private void SetupVelocityUniforms(
        int programId,
        float[] invCurrViewProj,
        float[] prevViewProj,
        int historyValid,
        int depthUnit = 0)
    {
        GL.UseProgram(programId);

        // Samplers
        GL.Uniform1(GL.GetUniformLocation(programId, "primaryDepth"), depthUnit);

        // Screen
        GL.Uniform2(GL.GetUniformLocation(programId, "screenSize"), (float)ScreenWidth, (float)ScreenHeight);

        // Matrices
        ShaderTestFramework.SetUniformMatrix4(GL.GetUniformLocation(programId, "invCurrViewProjMatrix"), invCurrViewProj);
        ShaderTestFramework.SetUniformMatrix4(GL.GetUniformLocation(programId, "prevViewProjMatrix"), prevViewProj);

        // History validity
        GL.Uniform1(GL.GetUniformLocation(programId, "historyValid"), historyValid);

        GL.UseProgram(0);
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

    [Fact]
    public void Velocity_StaticCamera_IsZero()
    {
        EnsureShaderTestAvailable();

        int programId = CompileVelocityShader();

        // Depth that is not sky and not zero.
        var depthData = CreateUniformDepthData(ScreenWidth, ScreenHeight, depth: 0.5f);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);

        using var outRt = TestFramework.CreateTestGBuffer(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba32f);

        float[] invCurr = LumOnTestInputFactory.CreateIdentityMatrix();
        float[] prev = LumOnTestInputFactory.CreateIdentityMatrix();

        SetupVelocityUniforms(programId, invCurr, prev, historyValid: 1);

        depthTex.Bind(0);
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

        GL.DeleteProgram(programId);
    }

    [Fact]
    public void Velocity_PureTranslation_ProducesExpectedDirection()
    {
        EnsureShaderTestAvailable();

        int programId = CompileVelocityShader();

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

        depthTex.Bind(0);
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

        GL.DeleteProgram(programId);
    }

    [Fact]
    public void Velocity_PureRotation_ProducesExpectedPattern()
    {
        EnsureShaderTestAvailable();

        int programId = CompileVelocityShader();

        var depthData = CreateUniformDepthData(ScreenWidth, ScreenHeight, depth: 0.5f);
        using var depthTex = TestFramework.CreateTexture(ScreenWidth, ScreenHeight, PixelInternalFormat.R32f, depthData);

        using var outRt = TestFramework.CreateTestGBuffer(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba32f);

        float[] invCurr = LumOnTestInputFactory.CreateIdentityMatrix();
        float[] prev = CreateRotationZ(radians: 10f * MathF.PI / 180f);

        SetupVelocityUniforms(programId, invCurr, prev, historyValid: 1);

        depthTex.Bind(0);
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

        GL.DeleteProgram(programId);
    }
}
