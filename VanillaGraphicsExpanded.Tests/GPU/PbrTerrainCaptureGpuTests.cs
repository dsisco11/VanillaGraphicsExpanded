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
    [InlineData(false, false, false, 0f, 0f, 1f, 0f)]
    [InlineData(true, false, false, 0f, 0f, 0f, 1f)]
    [InlineData(true, false, true, 0f, 0f, 0f, -1f)]
    [InlineData(true, true, false, 0f, 0f, 0.7071068f, 0.7071068f)]
    [InlineData(true, true, true, 0f, 0f, -0.7071068f, -0.7071068f)]
    [InlineData(false, true, false, .35f, 0f, 0f, 1f)]
    [InlineData(true, true, true, .35f, 0f, 0f, -1f)]
    [InlineData(false, false, true, .01f, 0f, 0f, -1f)]
    public void SurfaceNormalUsesGeometryForTransmissionOrLegacyUp(bool twoSided, bool smooth, bool back, float transmission, float x, float y, float z)
    {
        EnsureContextValid();
        string helper = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes", "vge_terrain_normal.glsl"));
        string authored = smooth ? "normalize(vec3(0,1,1))" : "vec3(0,1,0)";
        int fragment = Compile(ShaderType.FragmentShader, "#version 330 core\n" + helper + $$"""
            uniform float transmission;
            in vec3 world;
            layout(location=0) out vec4 result;
            void main() { result = vec4(VgeTerrainNormal({{authored}}, world, transmission), 1); }
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
            GL.Uniform1(GL.GetUniformLocation(program, "transmission"), transmission);
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

    #region Thin foliage transmission
    /// <summary>Transmission respects shadow visibility and surface direction, with a broad viewing lobe.</summary>
    [Theory]
    [InlineData(1f, false, false, 1f)]
    [InlineData(0f, false, false, 0f)]
    [InlineData(.5f, false, false, .5f)]
    [InlineData(1f, true, false, 0f)]
    [InlineData(1f, false, true, .25f)]
    public void TransmissionRespectsVisibilityAndAngles(float visibility, bool frontLit, bool sideView, float factor)
    {
        EnsureContextValid();
        string helper = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes", "pbr_transmission.glsl"));
        int fragment = Compile(ShaderType.FragmentShader, "#version 330 core\n" + helper + """
            uniform float visibility;
            uniform vec3 lightDirection;
            uniform vec3 viewDirection;
            layout(location=0) out vec4 result;
            void main() { result = vec4(VgeTransmission(vec3(.2,.8,.1), vec3(0,0,1),
                viewDirection, lightDirection, vec3(1), 0, .5, visibility), 1); }
            """);
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
            GL.UseProgram(program);
            GL.Uniform1(GL.GetUniformLocation(program, "visibility"), visibility);
            GL.Uniform3(GL.GetUniformLocation(program, "lightDirection"), 0f, 0f, frontLit ? 1f : -1f);
            GL.Uniform3(GL.GetUniformLocation(program, "viewDirection"), sideView ? 1f : 0f, 0f, sideView ? 0f : 1f);
            using var framework = new ShaderTestFramework();
            using var output = framework.CreateTestGBuffer(1, 1, PixelInternalFormat.Rgba32f);
            framework.RenderQuadTo(program, output);
            float[] actual = output[0].ReadPixels();
            float[] tint = [.2f, .8f, .1f];
            for (int channel = 0; channel < 3; channel++)
            {
                float expected = tint[channel] * .5f * factor / MathF.PI;
                Assert.InRange(actual[channel], expected - .00001f, expected + .00001f);
            }
        }
        finally { GL.DeleteProgram(program); GL.DeleteShader(vertex); GL.DeleteShader(fragment); }
    }
    #endregion

    #region Liquid interface optics
    /// <summary>Checks water reflectance, underwater total internal reflection and bounded missing-depth fallback.</summary>
    [Theory]
    [InlineData(1f, false, .0203732f, 16f)]
    [InlineData(1f, true, .0203732f, 0f)]
    [InlineData(.5f, true, 1f, 0f)]
    [InlineData(0f, false, 1f, 16f)]
    public void LiquidOpticsRemainBounded(float cosine, bool underwater, float reflection, float missingThickness)
    {
        EnsureContextValid();
        string helper = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes", "pbr_liquid_optics.glsl"));
        int fragment = Compile(ShaderType.FragmentShader, "#version 330 core\nconst float zNear=.1; const float zFar=100.0;\n" + helper + """
            uniform float cosine;
            uniform bool underwater;
            layout(location=0) out vec4 result;
            void main() { result = vec4(VgeLiquidFresnel(cosine, underwater),
                VgeLiquidThickness(1.0,.5,vec3(0,0,-1),underwater),
                VgeLiquidThickness(.4,.5,vec3(0,0,-1),underwater),
                VgeLiquidThickness(.75,.5,vec3(0,0,-1),underwater)); }
            """);
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
            GL.UseProgram(program);
            GL.Uniform1(GL.GetUniformLocation(program, "cosine"), cosine);
            GL.Uniform1(GL.GetUniformLocation(program, "underwater"), underwater ? 1 : 0);
            using var framework = new ShaderTestFramework();
            using var output = framework.CreateTestGBuffer(1, 1, PixelInternalFormat.Rgba32f);
            framework.RenderQuadTo(program, output);
            float[] actual = output[0].ReadPixels();
            Assert.InRange(actual[0], reflection - .00001f, reflection + .00001f);
            Assert.Equal(missingThickness, actual[1]);
            Assert.Equal(0f, actual[2]);
            if (underwater) Assert.Equal(0f, actual[3]);
            else Assert.InRange(actual[3], .19f, .21f);
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
