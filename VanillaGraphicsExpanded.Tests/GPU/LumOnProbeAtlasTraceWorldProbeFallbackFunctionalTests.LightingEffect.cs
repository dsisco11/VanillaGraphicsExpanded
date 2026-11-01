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
            ImageAssertions.Predicate(output[0],"""
                precise vec3 difference=b.rgb-c.rgb;
                precise float delta=(difference.r*.2126+difference.g*.7152)+difference.b*.0722;
                precise float brightness=abs(delta)/(1+abs(delta));
                vec3 expected=brightness*vec3(delta>=0?1:0,.35,delta<0?1:0);
                bool response=all(greaterThanEqual(a.rgb,expected-vec3(.002))) && all(lessThanEqual(a.rgb,expected+vec3(.002)));
                precise float energy=(a.r+a.g)+a.b;
                return bvec2(response && (multipliers.x==1?energy>.01:all(equal(a.rgb,vec3(0)))),false);
                """,normalTexture,suppressedTexture,parameters:new(expectLighting?1:0,0,0,0)).AssertEqual();
        }
    }
    #endregion
}
