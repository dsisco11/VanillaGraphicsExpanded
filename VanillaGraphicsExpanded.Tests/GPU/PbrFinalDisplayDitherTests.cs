using OpenTK.Graphics.OpenGL;
using TinyTokenizer.Ast;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Validates dithering for newly added final-postprocess gradients and already quantized scene inputs.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class PbrFinalDisplayDitherTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Quantized final output
    /// <summary>The installed GLSL 330 final stage links with bloom and god rays enabled after production patch expansion.</summary>
    [Fact]
    public void InstalledFinalPostprocessingLinks()
    {
        EnsureContextValid();
        using var shaders = new TerrainShaderTestFixture();
        string game = Environment.GetEnvironmentVariable("VINTAGE_STORY")!;
        int vertex = shaders.Compile(ShaderType.VertexShader,
            File.ReadAllText(Path.Combine(game, "assets/game/shaders/final.vsh")));
        string fragmentSource = PbrSurfaceInstalledShaderTests.Build("final.fsh", 1, 0, 1, 0, 0)
            .Replace("#version 330 core", "#version 330 core\n#define BLOOM 1\n#define GODRAYS 1\n#define FXAA 1\n");
        int fragment = shaders.Compile(ShaderType.FragmentShader, fragmentSource);
        using var program = GpuProgramObject.Adopt(TerrainShaderTestFixture.Link(vertex, fragment));
    }

    /// <summary>Added halo values retain fractional brightness while identity inputs retain their original codes and alpha.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FinalBoundaryPreservesCodesAndResolvesHaloGradient(bool addHalo)
    {
        EnsureContextValid();
        using var shaders = new TerrainShaderTestFixture();
        int vertex = shaders.Compile(ShaderType.VertexShader, """
            #version 430 core
            void main() {
                vec2 p[3]=vec2[3](vec2(-1,-1),vec2(3,-1),vec2(-1,3));
                gl_Position=vec4(p[gl_VertexID],0,1);
            }
            """);
        var tree = SyntaxTree.Parse("""
            #version 430 core
            layout(location=0) out vec4 outColor;
            void main() {
                float tile=floor(gl_FragCoord.x/8.0);
                outColor=vec4(vec3(100.0/255.0),73.0/255.0);
            #if ADD_HALO
                outColor.rgb+=vec3((tile+0.5)/8.0/255.0);
            #else
                outColor.rgb=vec3(tile/255.0);
            #endif
            }
            """, GlslSchema.Instance);
        PbrFinalDisplayPatches.Apply(tree);
        string helper = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets/shaders/includes/pbr_color.glsl"));
        string source = tree.ToText().Replace("#version 430 core", "#version 430 core\n#define ADD_HALO " + (addHalo ? "1" : "0") + "\n" + helper);
        int fragment = shaders.Compile(ShaderType.FragmentShader, source);
        using var program = GpuProgramObject.Adopt(TerrainShaderTestFixture.Link(vertex, fragment));
        using var vao = GpuVao.Create();
        using var framework = new ShaderTestFramework();
        int tiles = addHalo ? 8 : 256;
        int width = tiles * 8;
        using var target = framework.CreateTestGBuffer(width, 8, PixelInternalFormat.Rgba8);
        target.BindWithViewport();
        StateCache.Current.UseProgram(program.ProgramId);
        StateCache.Current.BindVertexArray(vao.VertexArrayId);
        GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.CullFace); GL.Disable(EnableCap.FramebufferSrgb);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        float[] actual = target[0].ReadPixels();
        int previous = -1;
        for (int tile = 0; tile < tiles; tile++)
        {
            int upper = 0;
            for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                int offset = (y * width + tile * 8 + x) * 4;
                int code = (int)MathF.Round(actual[offset] * 255);
                Assert.InRange(code, addHalo ? 100 : tile, addHalo ? 101 : tile);
                upper += code - 100;
                Assert.Equal(actual[offset], actual[offset + 1]);
                Assert.Equal(actual[offset], actual[offset + 2]);
                Assert.Equal(73, (int)MathF.Round(actual[offset + 3] * 255));
            }
            if (addHalo)
            {
                Assert.True(upper > previous);
                Assert.InRange(MathF.Abs(upper / 64f - (tile + .5f) / 8f), 0, 1f / 16f);
                previous = upper;
            }
        }
    }
    #endregion
}




