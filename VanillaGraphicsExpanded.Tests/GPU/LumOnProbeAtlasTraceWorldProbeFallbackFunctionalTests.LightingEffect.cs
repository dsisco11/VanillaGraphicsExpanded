using VanillaGraphicsExpanded.Tests.GPU.Assertions;
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
        bool expectLighting)
    {
        var program = Programs.Create<LumOnDebugShaderProgram>(identity: LumOnDebugShaderProgramFamily.GetProgramName(LumOnDebugMode.WorldProbeLightingEffect));
        {
            // Borrow simultaneous gather outputs directly; no upload copies or duplicate lighting managers.
            program.DebugMode = 43;
            program.WorldProbeEffectGain = 1;
            UpdateAndBindLumOnFrameUbo(program);
            program.IndirectDiffuseFull = normalTexture;
            program.WorldProbeSuppressedLighting = suppressedTexture;
            TestFramework.RenderQuadTo(program, output);
            ImageAssertions.Predicate(output[0], GpuAssertionPredicate.LumOnProbeAtlasTraceWorldProbeFallbackFunctionalTestsLightingEffect_AssertGatherLightingEffect,normalTexture,suppressedTexture,parameters:new(expectLighting?1:0,0,0,0)).AssertEqual();
        }
    }
    #endregion
}
