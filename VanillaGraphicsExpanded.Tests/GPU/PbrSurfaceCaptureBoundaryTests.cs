using OpenTK.Graphics.OpenGL;
using TinyTokenizer.Ast;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises production helper interception independently of engine-local variable names.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class PbrSurfaceCaptureBoundaryTests : RenderTestBase
{
    /// <summary>Uses the shared headless context.</summary>
    public PbrSurfaceCaptureBoundaryTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Capture behavior
    /// <summary>Scene capture ignores later underwater mutations, applies damage once, and preserves the original GUI path.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void CaptureFollowsLightingInputAndPreservesGui(int route, bool renamed)
    {
        EnsureContextValid();
        string source = """
            #version 330 core
            #define USEOIT 0
            #define NORMALVIEW 0
            #define BLOOM 0
            layout(location=0) out vec4 outColor;
            vec3 normal = vec3(0,0,1);
            int renderFlags = 0;
            float glowLevel = 0.0;
            float fogAmount = 0.0;
            float getMatMetallicFromRenderFlags(int flags) { return 0.0; }
            vec3 VgeLocalEnvironment(vec3 block, vec3 sky) { return block + sky * 0.35; }
            vec3 VgeForwardSurface(vec3 color, vec3 n, vec3 p, float fog, float transmission) { return color; }
            vec4 applyFogAndShadow(vec4 rgbaPixel, float fog) { return vec4(rgbaPixel.rgb * 0.5, rgbaPixel.a); }
            void main()
            {
                float b = 0.5;
                outColor = vec4(0.2,0.4,0.6,0.25);
            #if BLOOM == 0
                outColor.rgb *= 1 + glowLevel;
            #endif
                float murkiness = 1.0;
                if (murkiness > 0.0)
                {
                    outColor = applyFogAndShadow(outColor, 0.0);
                    outColor.rgb += vec3(1.0);
                }
                else outColor = applyFogAndShadow(outColor, fogAmount);
                outColor.rgb *= b;
            }
            """;
        if (renamed) source = source.Replace("float murkiness = 1.0;", "").Replace("murkiness", "changedWaterVariable")
            .Replace("float b = 0.5;", "float changedWaterVariable = 1.0; float b = 0.5;");
        string helper = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets/shaders/includes/pbr_color.glsl"));
        source = source.Replace("layout(location=0)", helper + "\nlayout(location=0)");
        var tree = SyntaxTree.Parse(source, GlslSchema.Instance);
        PbrSurfaceShaderPatches.Apply(tree, "standard.fsh");
        int vertex = 0, fragment = 0, program = 0;
        try
        {
            vertex = Compile(ShaderType.VertexShader, "#version 330 core\nlayout(location=0) in vec2 position;void main(){gl_Position=vec4(position,0,1);}");
            fragment = Compile(ShaderType.FragmentShader, tree.ToText());
            program = GL.CreateProgram();
            GL.AttachShader(program, vertex); GL.AttachShader(program, fragment); GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.True(linked != 0, GL.GetProgramInfoLog(program));
            GL.UseProgram(program);
            GL.Uniform1(GL.GetUniformLocation(program, "vge_pbrRoute"), route);
            using var framework = new ShaderTestFramework();
            using var output = framework.CreateTestGBuffer(1, 1, PixelInternalFormat.Rgba32f);
            framework.RenderQuadTo(program, output);
            float[] actual = output[0].ReadPixels();
            for (int channel = 0; channel < 3; channel++)
            {
                float encoded = (channel + 1) * .1f;
                float expected = route == 0 ? (encoded + 1f) * .5f : MathF.Pow((encoded + .055f) / 1.055f, 2.4f);
                Assert.InRange(actual[channel], expected - .00001f, expected + .00001f);
            }
            Assert.InRange(actual[3], .24999f, .25001f);
        }
        finally
        {
            if (program != 0) GL.DeleteProgram(program);
            if (fragment != 0) GL.DeleteShader(fragment);
            if (vertex != 0) GL.DeleteShader(vertex);
        }
    }

    #endregion

    #region Driver compilation
    /// <summary>Compiles transformed engine-style GLSL and disposes failed shader handles.</summary>
    private static int Compile(ShaderType type, string source)
    {
        int shader = GL.CreateShader(type);
        GL.ShaderSource(shader, source); GL.CompileShader(shader);
        GL.GetShader(shader, ShaderParameter.CompileStatus, out int compiled);
        string log = GL.GetShaderInfoLog(shader);
        if (compiled == 0) GL.DeleteShader(shader);
        Assert.True(compiled != 0, log);
        return shader;
    }
    #endregion
}
