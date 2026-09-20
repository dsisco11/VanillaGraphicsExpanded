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
    /// <summary>Checks sign colors, zero, mixed RGB luminance, display gain, and unavailable comparison data.</summary>
    [Theory]
    [InlineData(2f, 2f, 2f, 1f, true)]
    [InlineData(-2f, -2f, -2f, 1f, true)]
    [InlineData(0f, 0f, 0f, 1000f, true)]
    [InlineData(2f, -2f, 0f, 1f, true)]
    [InlineData(0.01f, 0.01f, 0.01f, 1f, true)]
    [InlineData(0.01f, 0.01f, 0.01f, 100f, true)]
    [InlineData(-0.01f, -0.01f, -0.01f, 100f, true)]
    [InlineData(2f, 2f, 2f, 100f, false)]
    public void LightingEffect_MapsSignedLuminance_AndIndicatesMissingPair(
        float red, float green, float blue, float gain, bool ready)
    {
        EnsureShaderTestAvailable();
        int program = CompileShader("lumon_debug.vsh", "lumon_debug_worldprobe.fsh");
        try
        {
            // Split signed differences into nonnegative HDR inputs.
            float[] positive = [Math.Max(red, 0), Math.Max(green, 0), Math.Max(blue, 0), 1];
            float[] negative = [Math.Max(-red, 0), Math.Max(-green, 0), Math.Max(-blue, 0), 1];
            using var normal = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, positive);
            using var suppressed = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba16f, negative);
            using var output = TestFramework.CreateTestGBuffer(ScreenWidth, ScreenHeight, PixelInternalFormat.Rgba16f);
            using var objectParams = new ObjectParamsUbo("Tests.WorldProbeLightingEffect");
            // Set gain first to also detect accidental overwrites by the neighboring UBO setters.
            var cpuParams = new LumOnDebugParamsUbo
            {
                DebugMode = 43, WorldProbeEffectGain = gain,
                WorldProbeComparisonReady = ready, DiffuseAOStrength = 0.5f, SpecularAOStrength = 0.5f
            };
            Assert.Equal(gain, cpuParams.WorldProbeEffectGain);
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
            float luminance = red * 0.2126f + green * 0.7152f + blue * 0.0722f;
            float magnitude = Math.Abs(luminance) * gain;
            float brightness = magnitude / (1 + magnitude);
            float[] expected = !ready ? [0.5f, 0, 0.5f]
                : luminance >= 0 ? [brightness, brightness * 0.35f, 0] : [0, brightness * 0.35f, brightness];
            for (int i = 0; i < pixels.Length; i += 4)
                for (int channel = 0; channel < 3; channel++)
                    Assert.InRange(pixels[i + channel], expected[channel] - TestEpsilon, expected[channel] + TestEpsilon);
        }
        finally { GL.DeleteProgram(program); }
    }
    #endregion
}