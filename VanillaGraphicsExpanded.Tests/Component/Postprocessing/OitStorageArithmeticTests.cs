using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using TinyTokenizer.Ast;
using VanillaGraphicsExpanded.Rendering;

using Assertions = VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Tests.Support.Lighting;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

namespace VanillaGraphicsExpanded.Tests.Component.Postprocessing;

/// <summary>Qualifies the installed bucket and resolve arithmetic separately from per-fragment storage containment.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class OitStorageArithmeticTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{


    #region Public API
    /// <summary>The owned writer discards invalid color, alpha and glow without modifying any destination attachment.</summary>
    [Fact]
    public void OwnedWriterRejectsExceptionalInputs()
    {
        EnsureContextValid();
        // Arrange: the packaged probe imports the shipped writer and owns a typed numeric input block.
        using var programs = new ComponentShaderPrograms();
        var program = programs.Create<OitOwnedWriterProbeProgram>();
        using var framework = new ShaderTestFramework();
        using var output = CreateMRTRenderTarget(1, 1, Enumerable.Repeat(TextureFormat.Rgba32f, 6).ToArray());
        var sentinel=(.125f,.25f,.5f,.75f);
        foreach (float invalidAlpha in new[] { -.1f, 1.1f, float.NaN, float.PositiveInfinity })
        {
            program.Capture(new(2f,1f,.5f,invalidAlpha),0f);

            // Act: invalid alpha discards the owned fragment through typed RHI submission.
            framework.RenderQuadTo(program,output,sentinel);

            // Assert: every destination attachment retains its clear sentinel.
            for (int i=0; i<6; i++) Assert.Equal(new[] {.125f,.25f,.5f,.75f}, output[i].ReadPixels());
        }
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity })
        {
            program.Capture(new(invalid,1f,.5f,.75f),0f);

            // Act: nonfinite radiance is rejected before any MRT output is written.
            framework.RenderQuadTo(program,output,sentinel);
            for (int i=0; i<6; i++) Assert.Equal(new[] {.125f,.25f,.5f,.75f}, output[i].ReadPixels());
            program.Capture(new(2f,1f,.5f,.75f),invalid);

            // Act: nonfinite glow is rejected while color remains otherwise valid.
            framework.RenderQuadTo(program,output,sentinel);
            for (int i=0; i<6; i++) Assert.Equal(new[] {.125f,.25f,.5f,.75f}, output[i].ReadPixels());
        }

        // Act: a valid color reaches the original accumulation output.
        program.Capture(new(2f,1f,.5f,.75f),0f);
        framework.RenderQuadTo(program,output);

        // Assert: preserve the original unweighted single-fragment accumulation oracle.
        Assert.Equal(new[] {1.5f,.75f,.375f,.75f}, output[3].ReadPixels());
    }

    #endregion

    #region Protected API
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
        programs.Prepare("inspection", 0, 0);
        programs.Prepare("predicate-material", 0, 0);
    }
    #endregion

    #region Private
/// <summary>Links a minimal full-screen interface while keeping installed fragment arithmetic intact.</summary>
    private static GpuProgramObject Link(string fragment)
    {
        string vertex = """
            #version 430 core
            layout(location=0) in vec2 position;
            out vec2 v_texcoord;
            void main(){gl_Position=vec4(position,0,1);v_texcoord=vec2(.5);}
            """;
        return GpuProgramObject.Adopt(TestShaderCache.CreateGraphics(vertex, fragment));
    }

    #endregion
}
