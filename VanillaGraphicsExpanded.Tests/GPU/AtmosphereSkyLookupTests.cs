using System.Collections.Immutable;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using TinyTokenizer.Ast;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Executes the production sky patch against a published, filtered atmospheric texture.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class AtmosphereSkyLookupTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Lookup transport
    /// <summary>Checks endpoint addressing, depressed horizon and periodic azimuth using actual shader lookup.</summary>
    [Theory]
    [InlineData(.001f, 24, false)]
    [InlineData(.001f, 24, true)]
    [InlineData(99f, 9, false)]
    [InlineData(99f, 9, true)]
    public void PatchedLookupReconstructsRowsAndWrapsSeam(float altitude, int height, bool displayTransfer)
    {
        EnsureContextValid();
        using var shaders = new TerrainShaderTestFixture();
        int vertex = shaders.Compile(ShaderType.VertexShader, """
            #version 430 core
            void main() {
                vec2 p[3] = vec2[3](vec2(-1,-1),vec2(3,-1),vec2(-1,3));
                gl_Position = vec4(p[gl_VertexID],0,1);
            }
            """);
        string mapping = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets/shaders/includes/atmosphere_sky_mapping.glsl"));
        // Exercise both isolated lookup transport and the actual shared display transfer.
        string transfer = displayTransfer
            ? File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets/shaders/includes/pbr_color.glsl"))
            : "vec3 VgeResolveDisplay(vec3 radiance) { return radiance; }\n";
        var tree = SyntaxTree.Parse("#version 430 core\n" + mapping + "\n" + transfer + "\n" + """
            uniform vec3 sampleDirection;
            vec4 skyColor; vec4 skyGlow;
            layout(location=0) out vec4 result;
            void getSkyColorAt(vec3 skyPosition) { skyColor=vec4(0,0,0,1); }
            void main() { getSkyColorAt(sampleDirection); result=skyColor; }
            """, GlslSchema.Instance);
        AtmosphereSkyPatches.Apply(tree);
        int fragment = shaders.Compile(ShaderType.FragmentShader, tree.ToText());
        using var program = GpuProgramObject.Adopt(TerrainShaderTestFixture.Link(vertex, fragment));
        using var vao = GpuVao.Create();
        using var framework = new ShaderTestFramework();
        using var target = framework.CreateTestGBuffer(1, 1, PixelInternalFormat.Rgba32f);
        using var owner = new AtmosphereModSystem();
        const int width = 4;
        float[] pixels = new float[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int i = (y * width + x) * 4;
            pixels[i] = (float)y / (height - 1); pixels[i + 1] = x * .25f; pixels[i + 3] = 1;
        }
        float horizon = AtmosphereSkyMapping.Horizon(altitude);
        owner.Publish(new(Vector3.UnitY, Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero, ImmutableArray.CreateRange(pixels))
            { Width = width, Height = height, HorizonElevation = horizon });
        var layout = GpuProgramLayout.TryBuild(program.ProgramId);
        target.BindWithViewport();
        GlStateCache.Current.UseProgram(program.ProgramId); GlStateCache.Current.BindVertexArray(vao.VertexArrayId);
        using var binding = GlStateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, AtmosphereModSystem.SkyTextureId);
        ShaderTestFramework.SetUniform(layout.GetUniformLocation(program.ProgramId, "vge_atmosphereSky"), 0);
        ShaderTestFramework.SetUniform(layout.GetUniformLocation(program.ProgramId, "vge_atmosphereLutHorizon"), horizon);
        GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.CullFace);
        foreach (float v in new[] { 0f, .25f, .499f, .5f, .501f, .75f, 1f })
        foreach (float azimuth in new[] { -.00001f, 0f, .00001f })
        {
            target.BindWithViewport();
            float elevation = AtmosphereSkyMapping.Elevation(v, horizon);
            Vector3 direction = new(MathF.Cos(elevation) * MathF.Cos(azimuth), MathF.Sin(elevation), MathF.Cos(elevation) * MathF.Sin(azimuth));
            ShaderTestFramework.SetUniform(layout.GetUniformLocation(program.ProgramId, "sampleDirection"), direction.X, direction.Y, direction.Z);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            float[] actual = target[0].ReadPixels();
            float expectedRed = displayTransfer ? Transfer(v, Math.Max(v, .375f)) : v;
            float expectedGreen = displayTransfer ? Transfer(.375f, Math.Max(v, .375f)) : .375f;
            Assert.True(MathF.Abs(actual[0] - expectedRed) <= .0006f, $"v={v}, azimuth={azimuth}, actual={actual[0]}");
            Assert.InRange(MathF.Abs(actual[1] - expectedGreen), 0, .0001f);
            Assert.Equal(1, actual[3]);
        }
    }
    /// <summary>Evaluates the display boundary independently of the GLSL helper.</summary>
    private static float Transfer(float value, float peak)
    {
        float mapped = value / (1 + peak);
        return mapped <= .0031308f ? 12.92f * mapped : 1.055f * MathF.Pow(mapped, 1 / 2.4f) - .055f;
    }
    #endregion
}
