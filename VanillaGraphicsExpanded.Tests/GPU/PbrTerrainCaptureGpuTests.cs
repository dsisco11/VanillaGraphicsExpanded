using OpenTK.Graphics.OpenGL;
using TinyTokenizer.Ast;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Compiles transformed engine GLSL to verify capture scope and material output numerically.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class PbrTerrainCaptureGpuTests : RenderTestBase
{
    /// <summary>Uses the shared context without loading a game process.</summary>
    public PbrTerrainCaptureGpuTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Engine material capture
    /// <summary>Production patching restores decoded material RGB after forward effects while preserving output alpha.</summary>
    [Theory]
    [InlineData("chunkopaque.fsh")]
    [InlineData("chunktopsoil.fsh")]
    public void TransformedEngineMainCompilesAndCapturesMaterial(string name)
    {
        EnsureContextValid();
        string helper = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes", "pbr_color.glsl"));
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
        PbrTerrainColorPatches.ApplyFragment(tree, name);
        // The base game's shaders are runtime GLSL, so this deliberately exercises that compilation
        // boundary rather than registering an unrelated precompiled replacement fixture.
        int fragment = Compile(ShaderType.FragmentShader, tree.ToText());
        int vertex = Compile(ShaderType.VertexShader, """
            #version 330 core
            layout(location=0) in vec2 position;
            void main() { gl_Position = vec4(position,0,1); }
            """);
        int program = GL.CreateProgram();
        try
        {
            GL.AttachShader(program, vertex); GL.AttachShader(program, fragment); GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.True(linked != 0, GL.GetProgramInfoLog(program));
            using var framework = new ShaderTestFramework();
            using var output = framework.CreateTestGBuffer(1, 1, PixelInternalFormat.Rgba32f);
            framework.RenderQuadTo(program, output);
            float[] actual = output[0].ReadPixels();
            float[] expected = [Linear(.2f), Linear(.4f), Linear(.8f), .25f];
            for (int channel = 0; channel < 4; channel++) Assert.InRange(actual[channel], expected[channel] - .00001f, expected[channel] + .00001f);
        }
        finally { GL.DeleteProgram(program); GL.DeleteShader(vertex); GL.DeleteShader(fragment); }
    }
    #endregion

    #region Two-sided foliage normals
    /// <summary>Real derivatives correct fake UP normals while preserving opaque and authored smooth normals.</summary>
    [Theory]
    [InlineData(false, false, false, 0f, 1f, 0f)]
    [InlineData(true, false, false, 0f, 0f, 1f)]
    [InlineData(true, false, true, 0f, 0f, -1f)]
    [InlineData(true, true, false, 0f, 0.7071068f, 0.7071068f)]
    [InlineData(true, true, true, 0f, -0.7071068f, -0.7071068f)]
    public void FoliageNormalUsesGeometryOnlyForLegacyUp(bool twoSided, bool smooth, bool back, float x, float y, float z)
    {
        EnsureContextValid();
        string helper = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes", "vge_terrain_normal.glsl"));
        string authored = smooth ? "normalize(vec3(0,1,1))" : "vec3(0,1,0)";
        int fragment = Compile(ShaderType.FragmentShader, "#version 330 core\n" + helper + $$"""
            in vec3 world;
            layout(location=0) out vec4 result;
            void main() { result = vec4(VgeTerrainNormal({{authored}}, world), 1); }
            """);
        string side = back ? "-position.x" : "position.x";
        int vertex = Compile(ShaderType.VertexShader, $$"""
            #version 330 core
            layout(location=0) in vec2 position;
            out vec3 world;
            void main() { world = vec3({{side}},position.y,0); gl_Position = vec4(position,0,1); }
            """);
        int program = GL.CreateProgram();
        try
        {
            GL.AttachShader(program, vertex); GL.AttachShader(program, fragment); GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.True(linked != 0, GL.GetProgramInfoLog(program));
            GL.UseProgram(program);
            GL.Uniform1(GL.GetUniformLocation(program, "vge_twoSidedTerrain"), twoSided ? 1 : 0);
            using var framework = new ShaderTestFramework();
            using var output = framework.CreateTestGBuffer(4, 4, PixelInternalFormat.Rgba32f);
            framework.RenderQuadTo(program, output);
            float[] actual = output[0].ReadPixels();
            float[] expected = [x, y, z, 1];
            for (int channel = 0; channel < 4; channel++)
                Assert.InRange(actual[channel], expected[channel] - .00001f, expected[channel] + .00001f);
        }
        finally { GL.DeleteProgram(program); GL.DeleteShader(vertex); GL.DeleteShader(fragment); }
    }
    #endregion

    #region Runtime GLSL validation
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
