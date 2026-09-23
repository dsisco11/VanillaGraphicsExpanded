using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks the signed diagnostic using paired outputs from the production gather shaders.</summary>
public partial class LumOnProbeAtlasTraceWorldProbeFallbackFunctionalTests
{
    #region Diagnostic Output
    /// <summary>Renders gathered lighting directly at its native resolution to isolate the diagnostic from upsampling.</summary>
    private void AssertGatherLightingEffect(GpuTexture normalTexture, GpuTexture suppressedTexture, GpuFramebuffer output,
        float[] normal, float[] suppressed, bool expectLighting)
    {
        var program = Programs.Create<LumOnDebugShaderProgram>(identity: LumOnDebugShaderProgram.WorldprobeContract.Identity);
        using var use = program.UseScope();
        {
            // Borrow simultaneous gather outputs directly; no upload copies or duplicate lighting managers.
            program.DebugMode = 43;
            program.WorldProbeEffectGain = 1;
            UpdateAndBindLumOnFrameUbo(program);
            program.IndirectDiffuseFull = normalTexture;
            program.WorldProbeSuppressedLighting = suppressedTexture;
            TestFramework.RenderQuadTo(program, output);
            var pixels = output[0].ReadPixels();
            for (int i = 0; i < pixels.Length; i += 4)
            {
                for (int channel = 0; channel < 3; channel++)
                {
                    float delta = (normal[i] - suppressed[i]) * 0.2126f + (normal[i + 1] - suppressed[i + 1]) * 0.7152f + (normal[i + 2] - suppressed[i + 2]) * 0.0722f;
                    float brightness = Math.Abs(delta) / (1 + Math.Abs(delta));
                    float expected = brightness * (channel == 1 ? 0.35f : (delta >= 0 ? channel == 0 : channel == 2) ? 1f : 0f);
                    Assert.InRange(pixels[i + channel], expected - 0.002f, expected + 0.002f);
                }
                if (expectLighting)
                    Assert.True(pixels[i] + pixels[i + 1] + pixels[i + 2] > 0.01f);
                else
                    for (int channel = 0; channel < 3; channel++) Assert.Equal(0f, pixels[i + channel]);
            }
        }
    }
    #endregion
}
