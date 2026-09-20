using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks the paired diagnostic against known HDR differences on the GPU.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LumOnWorldProbeLightingEffectFunctionalTests : LumOnShaderFunctionalTestBase
{
    #region Construction
    /// <summary>Uses the shared headless OpenGL context.</summary>
    public LumOnWorldProbeLightingEffectFunctionalTests(HeadlessGLFixture fixture) : base(fixture) { }
    #endregion

    #region Signed Difference
    /// <summary>Positive, negative and zero differences must survive the signed debug mapping.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LightingEffect_MapsSignedLinearDifference_AndIndicatesMissingPair(bool ready)
    {
        EnsureShaderTestAvailable();
        int program = CompileShader("lumon_debug.vsh", "lumon_debug_worldprobe.fsh");
        try
        {
            using var normal = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new float[] { 3, 1, 2, 1 });
            using var suppressed = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, new float[] { 1, 3, 2, 1 });
            using var output = TestFramework.CreateTestGBuffer(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f);
            using var objectParams = new ObjectParamsUbo("Tests.WorldProbeLightingEffect");
            var cpuParams = new LumOnDebugParamsUbo { DebugMode = 43, WorldProbeComparisonReady = ready };
            UniformBlockBindingUtil.EnsureBlockBound(program, LumOnDebugParamsUbo.BlockName, GpuBindingRegistry.Ubo.Object);
            objectParams.UploadAndBind(cpuParams.Bytes);
            UpdateAndBindLumOnFrameUbo(program);
            normal.Bind(0);
            suppressed.Bind(1);
            GL.UseProgram(program);
            GL.Uniform1(GL.GetUniformLocation(program, "indirectDiffuseFull"), 0);
            GL.Uniform1(GL.GetUniformLocation(program, "worldProbeSuppressedLighting"), 1);
            GL.UseProgram(0);
            TestFramework.RenderQuadTo(program, output);
            var pixels = output[0].ReadPixels();
            // This difference is taken before tone mapping: (+2,-2,0) maps to (5/6,1/6,1/2).
            float[] expected = ready ? new float[] { 5f / 6f, 1f / 6f, 0.5f } : new float[] { 0.5f, 0f, 0.5f };
            for (int i = 0; i < pixels.Length; i += 4)
                for (int channel = 0; channel < 3; channel++)
                    Assert.InRange(pixels[i + channel], expected[channel] - TestEpsilon, expected[channel] + TestEpsilon);
        }
        finally { GL.DeleteProgram(program); }
    }
    #endregion
}
