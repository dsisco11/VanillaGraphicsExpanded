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
    private void AssertGatherLightingEffect(float[] normal, float[] suppressed, bool expectLighting)
    {
        int program = CompileShader("lumon_debug.vsh", "lumon_debug_worldprobe.fsh");
        try
        {
            using var normalTexture = TestFramework.CreateTexture(HalfResWidth, HalfResHeight, PixelInternalFormat.Rgba16f, normal);
            using var suppressedTexture = TestFramework.CreateTexture(HalfResWidth, HalfResHeight, PixelInternalFormat.Rgba16f, suppressed);
            using var output = TestFramework.CreateTestGBuffer(HalfResWidth, HalfResHeight, PixelInternalFormat.Rgba16f);
            using var parameters = new ObjectParamsUbo("Tests.SealedRoom.LightingEffect");
            var cpu = new LumOnDebugParamsUbo { DebugMode = 43, WorldProbeComparisonReady = true };
            UniformBlockBindingUtil.EnsureBlockBound(program, LumOnDebugParamsUbo.BlockName, GpuBindingRegistry.Ubo.Object);
            parameters.UploadAndBind(cpu.Bytes);
            UpdateAndBindLumOnFrameUbo(program);
            normalTexture.Bind(0);
            suppressedTexture.Bind(1);
            BindPipelineSamplers(program, ("indirectDiffuseFull", 0), ("worldProbeSuppressedLighting", 1));
            TestFramework.RenderQuadTo(program, output);
            var pixels = output[0].ReadPixels();
            for (int i = 0; i < pixels.Length; i += 4)
            {
                for (int channel = 0; channel < 3; channel++)
                {
                    float delta = normal[i + channel] - suppressed[i + channel];
                    float expected = 0.5f + 0.5f * delta / (1 + Math.Abs(delta));
                    Assert.InRange(pixels[i + channel], expected - 0.002f, expected + 0.002f);
                }
                if (expectLighting)
                    Assert.True(pixels[i] + pixels[i + 1] + pixels[i + 2] > 1.51f);
                else
                    for (int channel = 0; channel < 3; channel++) Assert.Equal(0.5f, pixels[i + channel]);
            }
        }
        finally { GL.DeleteProgram(program); }
    }
    #endregion
}
