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
        using var modules = new TerrainShaderTestFixture();
        int vertex = modules.Load(ShaderType.VertexShader, "tests/normal-input.vsh");
        int fragment = modules.Load(ShaderType.FragmentShader, "tests/normal-input.fsh");
        int program = GL.CreateProgram();
        try
        {
            GL.AttachShader(program, vertex); GL.AttachShader(program, fragment); GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.True(linked != 0, GL.GetProgramInfoLog(program));
            GL.UseProgram(program);
            using var inputs = new FixtureUniformInputs(16);
            inputs.Float(0, transmission);
            inputs.Integer(4, twoSided ? 1 : 0);
            inputs.Integer(8, smooth ? 1 : 0);
            inputs.Integer(12, back ? 1 : 0);
            inputs.Publish();
            using var framework = new ShaderTestFramework();
            using var output = framework.CreateTestGBuffer(4, 4, PixelInternalFormat.Rgba32f);
            framework.RenderQuadTo(program, output);
            float[] actual = output[0].ReadPixels();
            float[] expected = [x, y, z, 1];
            for (int channel = 0; channel < 4; channel++)
                Assert.InRange(actual[channel], expected[channel] - .00001f, expected[channel] + .00001f);
        }
        finally { GL.DeleteProgram(program); }
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
        using var modules = new TerrainShaderTestFixture();
        int vertex = modules.Load(ShaderType.VertexShader, "tests/numerical-quad.vsh");
        int fragment = modules.Load(ShaderType.FragmentShader, "tests/foliage-transmission.fsh");
        int program = GL.CreateProgram();
        try
        {
            GL.AttachShader(program, vertex); GL.AttachShader(program, fragment); GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.True(linked != 0, GL.GetProgramInfoLog(program));
            GL.UseProgram(program);
            using var inputs = new FixtureUniformInputs(48);
            inputs.Float(0, visibility);
            inputs.Vector(16, 0f, 0f, frontLit ? 1f : -1f);
            inputs.Vector(32, sideView ? 1f : 0f, 0f, sideView ? 0f : 1f);
            inputs.Publish();
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
        finally { GL.DeleteProgram(program); }
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
        using var modules = new TerrainShaderTestFixture();
        int vertex = modules.Load(ShaderType.VertexShader, "tests/numerical-quad.vsh");
        int fragment = modules.Load(ShaderType.FragmentShader, "tests/liquid-interface.fsh");
        int program = GL.CreateProgram();
        try
        {
            GL.AttachShader(program, vertex); GL.AttachShader(program, fragment); GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.True(linked != 0, GL.GetProgramInfoLog(program));
            GL.UseProgram(program);
            using var inputs = new FixtureUniformInputs(16);
            inputs.Float(0, cosine);
            inputs.Integer(4, underwater ? 1 : 0);
            inputs.Publish();
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
        finally { GL.DeleteProgram(program); }
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
