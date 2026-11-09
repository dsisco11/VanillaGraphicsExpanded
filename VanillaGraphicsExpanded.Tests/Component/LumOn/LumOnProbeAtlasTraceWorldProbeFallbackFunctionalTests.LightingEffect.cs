using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.LumOn;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Checks the signed diagnostic using paired outputs from the production gather shaders.</summary>
public partial class LumOnProbeAtlasTraceWorldProbeFallbackFunctionalTests
{
    #region Private
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
            ImageAssertions.Predicates.Predicate(output[0], SurfacePredicates.LumOnProbeAtlasTraceWorldProbeFallbackFunctionalTestsLightingEffect_AssertGatherLightingEffect,normalTexture,suppressedTexture,parameters:new(expectLighting?1:0,0,0,0)).AssertEqual();
        }
    }
    #endregion
    #endregion
}
