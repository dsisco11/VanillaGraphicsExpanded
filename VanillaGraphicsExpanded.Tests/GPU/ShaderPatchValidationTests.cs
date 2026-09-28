using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks engine definitions are present in temporary shader validation inputs.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class ShaderPatchValidationTests : RenderTestBase
{
    /// <summary>Uses the shared driver context for validation compilation.</summary>
    public ShaderPatchValidationTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Validation
    /// <summary>Reproduces the entity array-size rejection and resolves it with the engine prefix.</summary>
    [Fact]
    public void EngineAnimationPrefixMakesCandidateValid()
    {
        EnsureContextValid();
        const string source = """
            #version 330 core
            uniform mat4 values[MAXANIMATEDELEMENTS];
            void main() { gl_Position = values[0] * vec4(0.0, 0.0, 0.0, 1.0); }
            """;
        Assert.False(GlslCompileDiagnostics.TryCompileStage(ShaderType.VertexShader, source, out string failure));
        Assert.False(string.IsNullOrWhiteSpace(failure));
        string validation = ShaderIncludesHook.BuildValidationSource(source, "#define MAXANIMATEDELEMENTS 46\n");
        Assert.True(GlslCompileDiagnostics.TryCompileStage(ShaderType.VertexShader, validation, out string log), log);
        Assert.DoesNotContain("#define", source);
    }
    #endregion
}
