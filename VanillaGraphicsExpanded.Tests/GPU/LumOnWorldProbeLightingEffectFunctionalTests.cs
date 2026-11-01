using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.LumOn;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks the paired diagnostic against known HDR differences on the GPU.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
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
        var program = Programs.Create<LumOnDebugShaderProgram>(identity: LumOnDebugShaderProgramFamily.GetProgramName(LumOnDebugMode.WorldProbeLightingEffect));
        {
            // Split signed differences into nonnegative HDR inputs.
            float[] positive = [Math.Max(red, 0), Math.Max(green, 0), Math.Max(blue, 0), 1];
            float[] negative = [Math.Max(-red, 0), Math.Max(-green, 0), Math.Max(-blue, 0), 1];
            using var normal = TestFramework.CreateTexture(1, 1, TextureFormat.Rgba16f, positive);
            using var suppressed = TestFramework.CreateTexture(1, 1, TextureFormat.Rgba16f, negative);
            using var output = TestFramework.CreateTestGBuffer(ScreenWidth, ScreenHeight, TextureFormat.Rgba16f);
            // Apply adjacent setters after gain to catch parameter-buffer overwrites.
            program.WorldProbeEffectGain = gain;
            program.DebugMode = 43;
            program.DiffuseAOStrength = .5f;
            program.SpecularAOStrength = .5f;
            UpdateAndBindLumOnFrameUbo(program);
            program.IndirectDiffuseFull = normal;
            program.WorldProbeSuppressedLighting = ready ? suppressed : null;
            TestFramework.RenderQuadTo(program, output);

            float luminance = red * 0.2126f + green * 0.7152f + blue * 0.0722f;
            float magnitude = Math.Abs(luminance) * gain;
            float brightness = magnitude / (1 + magnitude);
            float[] expected = !ready ? [0.5f, 0, 0.5f]
                : luminance >= 0 ? [brightness, brightness * 0.35f, 0] : [0, brightness * 0.35f, brightness];
            ImageAssertions.Predicate(output[0],"return bvec2(all(greaterThanEqual(a.rgb,multipliers.rgb-vec3(multipliers.w))) && all(lessThanEqual(a.rgb,multipliers.rgb+vec3(multipliers.w))),false);",
                parameters:new(expected[0],expected[1],expected[2],TestEpsilon)).AssertEqual();
        }
    }
    #endregion
}
