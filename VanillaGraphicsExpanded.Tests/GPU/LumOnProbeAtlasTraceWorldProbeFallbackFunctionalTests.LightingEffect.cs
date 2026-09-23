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
    private void AssertGatherLightingEffect(float[] normal, float[] suppressed, bool expectLighting)
    {
        int program = CompileShader("lumon_debug.vsh", "lumon_debug_worldprobe.fsh");
        try
        {
            using var assets = new BinaryShaderApiFixture();
            using var normalBuffers = new LumOnBufferManager(assets.Api, new VgeConfig());
            using var suppressedBuffers = new LumOnBufferManager(assets.Api, new VgeConfig());
            normalBuffers.EnsureBuffers(HalfResWidth, HalfResHeight);
            suppressedBuffers.EnsureBuffers(HalfResWidth, HalfResHeight);
            var normalTexture = normalBuffers.IndirectFullTex!;
            var suppressedTexture = suppressedBuffers.IndirectFullTex!;
            normalTexture.UploadDataImmediate(normal);
            suppressedTexture.UploadDataImmediate(suppressed);
            using var terrain = new EngineTerrainBuffers(HalfResWidth, HalfResHeight);
            var output = terrain.Output;
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
        finally { global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.DeleteProgram(program); }
    }
    #endregion
}
