using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using TinyTokenizer.Ast;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Materials.Energy;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Shaders;



namespace VanillaGraphicsExpanded.Tests.Integration.Terrain;

/// <summary>Checks transformed engine terrain shaders compile and render material output.</summary>
[Collection("GPU" )]
[Trait("Category", "GpuIntegration" )]
public sealed class PbrTerrainCaptureGpuTests : RenderTestBase
{
    #region Public API

    /// <summary>Uses the shared context without loading a game process.</summary>
    public PbrTerrainCaptureGpuTests(HeadlessGLFixture fixture) : base(fixture) { }

/// <summary>Production patching restores decoded material RGB after forward effects after resolving opaque coverage.</summary>
    [Theory]
    [InlineData("chunkopaque.fsh")]
    [InlineData("chunktopsoil.fsh")]
    public void TransformedEngineMainCompilesAndCapturesMaterial(string name)
    {
        // Arrange: author an engine-style fragment body and load the production color helper for this material family.
        EnsureContextValid();
        string helper = TestAssetSnapshot.ReadBuiltText(Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes", "pbr_color.glsl"));
        string source = "#version 330 core\n" + helper + """

            layout(location=0) out vec4 outColor;
            void main()
            {
                vec4 texColor = vec4(0.2, 0.4, 0.8, 0.5);
                outColor = texColor;
                float murkiness = 0.0;
                outColor = vec4(9.0, 8.0, 7.0, 0.25);
            #if NORMALVIEW > 0
                outColor.rgb = vec3(0.0);
            #endif
            }
            """;
        var tree = SyntaxTree.Parse(source.ReplaceLineEndings("\r\n"), GlslSchema.Instance);
        // Act: apply the production source rewrite, compile both stages, link, and render the transformed shader.
        PbrTerrainColorPatches.ApplyFragment(tree, name);
        // The base game's shaders are runtime GLSL, so this deliberately exercises that compilation
        // boundary rather than registering an unrelated precompiled replacement fixture.
        int fragment = 0;
        int vertex = 0;
        int program = 0;
        try
        {
            fragment = Compile(ShaderType.FragmentShader, tree.ToText());
            vertex = Compile(ShaderType.VertexShader, """
                #version 330 core
                layout(location=0) in vec2 position;
                void main() { gl_Position = vec4(position,0,1); }
                """);
            program = GL.CreateProgram();
            GL.AttachShader(program, vertex); GL.AttachShader(program, fragment); GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            // Assert the rewritten executable linked before preparing the capture target.
            Assert.True(linked != 0, GL.GetProgramInfoLog(program));
            using var framework = new ShaderTestFramework();
            using var output = framework.CreateTestGBuffer(1, 1, TextureFormat.Rgba32f);
            // Act: render transformed engine material code through the native capture target.
            framework.RenderNativeReferenceQuadTo(program, output);
            float[] actual = output[0].ReadPixels();
            float[] expected = [Linear(.2f), Linear(.4f), Linear(.8f), 1f];
            // Assert decoded material RGB and opaque coverage after the render.
            for (int channel = 0; channel < 4; channel++) Assert.InRange(actual[channel], expected[channel] - .00001f, expected[channel] + .00001f);
        }
        finally
        {
            if (program != 0) GL.DeleteProgram(program);
            if (vertex != 0) GL.DeleteShader(vertex);
            if (fragment != 0) GL.DeleteShader(fragment);
        }
    }
    #endregion

    #region Private
/// <summary>Compiles actual transformed text and reports driver diagnostics on lexical or interface errors.</summary>
    private static int Compile(ShaderType type, string source)
    {
        int shader = GL.CreateShader(type);
        GL.ShaderSource(shader, source); GL.CompileShader(shader);
        GL.GetShader(shader, ShaderParameter.CompileStatus, out int compiled);
        if (compiled != 0) return shader;
        string diagnostics = GL.GetShaderInfoLog(shader);
        GL.DeleteShader(shader);
        Assert.Fail(diagnostics + "\n" + source);
        return 0;
    }

    /// <summary>Evaluates the material's specified transfer function independently.</summary>
    private static float Linear(float value) => value <= .04045f ? value / 12.92f : MathF.Pow((value + .055f) / 1.055f, 2.4f);
    #endregion
}
