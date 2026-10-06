using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Rejects a numeric input that bypasses the owned program's uniform-block contract.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class StandaloneNumericInputTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Native linked-interface inspection catches an uncontracted standalone numeric value.</summary>
    [Fact]
    public void PreparedInterfaceRejectsStandaloneNumericInput()
    {
        EnsureContextValid();
        using var shaders = new TerrainShaderTestFixture();
        // Deliberately compile an invalid owned interface; it must never reach a draw.
        int vertex = shaders.Compile(ShaderType.VertexShader,
            "#version 430 core\nvoid main(){gl_Position=vec4(0,0,0,1);}");
        int fragment = shaders.Compile(ShaderType.FragmentShader,
            "#version 430 core\nlayout(location=0) uniform float value;out vec4 color;void main(){color=vec4(value);}");
        using var program = GpuProgramObject.Adopt(TerrainShaderTestFixture.Link(vertex, fragment));
        var failure = Assert.Throws<InvalidOperationException>(() =>
            new GpuPreparedBindings(program.ProgramId, new GpuBindingContract()));
        Assert.Contains("must use a uniform block", failure.Message);
    }
    #endregion
}
