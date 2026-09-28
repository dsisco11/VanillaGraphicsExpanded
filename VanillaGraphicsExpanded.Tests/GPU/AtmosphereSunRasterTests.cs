using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Executes production solar geometry and fragment coverage through owned shader modules.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class AtmosphereSunRasterTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Disk rasterization
    /// <summary>The GPU segment model retains bounded geometry and CPU agreement at grazing incidence.</summary>
    [Fact]
    public void GrazingSegmentMathMatchesCpu()
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
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets/shaders/includes/atmosphere_solar_disk.glsl"));
        int fragment = shaders.Compile(ShaderType.FragmentShader, "#version 430 core\n" + source + """
            uniform float elevation;
            layout(location=0) out vec4 result;
            void main() {
                float visible=atmSunVisibility(elevation,0);
                result=vec4(visible,atmSunVisibleElevation(elevation,0,visible),0,1);
            }
            """);
        using var program = GpuProgramObject.Adopt(TerrainShaderTestFixture.Link(vertex, fragment));
        using var vao = GpuVao.Create();
        using var framework = new ShaderTestFramework();
        using var target = framework.CreateTestGBuffer(1, 1, PixelInternalFormat.Rgba32f);
        var layout = GpuProgramLayout.TryBuild(program.ProgramId);
        GlStateCache.Current.UseProgram(program.ProgramId);
        GlStateCache.Current.BindVertexArray(vao.VertexArrayId);
        GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.CullFace);
        for (int exponent = 2; exponent <= 7; exponent++)
        foreach (float sign in new[] { -1f, 1f })
        {
            float elevation = sign * (1 - MathF.Pow(10, -exponent)) * AtmosphereSolarDisk.AngularRadius;
            target.BindWithViewport();
            ShaderTestFramework.SetUniform(layout.GetUniformLocation(program.ProgramId, "elevation"), elevation);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            float[] actual = target[0].ReadPixels();
            float visible = AtmosphereSolarDisk.Visibility(elevation, 0);
            Assert.InRange(actual[0], 0, 1);
            Assert.True(MathF.Abs(actual[0] - visible) <= 1e-9f + visible * 1e-4f);
            Assert.InRange(actual[1], -1e-7f, elevation + AtmosphereSolarDisk.AngularRadius + 1e-7f);
            float expectedCentroid = AtmosphereSolarDisk.VisibleElevation(elevation, 0, visible);
            Assert.True(MathF.Abs(actual[1] - expectedCentroid) <= 1e-7f,
                $"e={elevation:G9}, visibility={visible:G9}, GPU={actual[1]:G9}, CPU={expectedCentroid:G9}");
        }
    }

    /// <summary>Solar geometry ignores camera translation and produces bounded disk coverage and HDR color.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DiskIgnoresCameraTranslationAndClipsAtHorizon(bool displayTransfer)
    {
        EnsureContextValid();
        using var shaders = new TerrainShaderTestFixture();
        string directory = Path.Combine(AppContext.BaseDirectory, "assets/shaders/includes");
        int vertex = shaders.Compile(ShaderType.VertexShader, """
            #version 430 core
            vec2 uvIn;
            mat4 projectionMatrix;
            mat4 viewMatrix;
            uniform vec3 camera;
            """ + File.ReadAllText(Path.Combine(directory, "atmosphere_sun_vertex.glsl")) + """
            void main() {
                vec2 corners[6] = vec2[6](vec2(0,0),vec2(1,0),vec2(1,1),vec2(0,0),vec2(1,1),vec2(0,1));
                uvIn=corners[gl_VertexID];
                float zoom=.75/tan(vge_atmosphereDisk.w);
                projectionMatrix=mat4(zoom,0,0,0, 0,zoom,0,0, 0,0,-1,-1, 0,0,-1,0);
                viewMatrix=mat4(1); viewMatrix[3]=vec4(camera,1);
                VgeDrawAtmosphericSun();
            }
            """);
        int fragment = shaders.Compile(ShaderType.FragmentShader, """
            #version 430 core
            #define VGE_SURFACE_PRIMARY_OUTPUTS 0
            #define SSAOLEVEL 0
            layout(location=0) out vec4 outColor;
            layout(location=1) out vec4 outGlow;
            const float extraGodray=1;
            float getSkyMurkiness() { return 0; }
            vec3 applyUnderwaterEffects(vec3 color,float murk) { return color; }
            """ + "\n" + (displayTransfer ? File.ReadAllText(Path.Combine(directory, "pbr_color.glsl"))
                : "vec3 VgeResolveDisplay(vec3 value) { return value; }\n")
            + "\n" + File.ReadAllText(Path.Combine(directory, "atmosphere_sun_fragment.glsl")) + "\n" + """
            void main() { VgeDrawAtmosphericSun(); }
            """);
        using var program = GpuProgramObject.Adopt(TerrainShaderTestFixture.Link(vertex, fragment));
        using var vao = GpuVao.Create();
        using var color = DynamicTexture2D.Create(64, 64, PixelInternalFormat.Rgba32f);
        using var depth = DynamicTexture2D.Create(64, 64, PixelInternalFormat.DepthComponent32f);
        using var target = GpuFramebuffer.CreateMRT([color], depth, ownsTextures: false)!;
        var layout = GpuProgramLayout.TryBuild(program.ProgramId);
        GlStateCache.Current.UseProgram(program.ProgramId);
        GlStateCache.Current.BindVertexArray(vao.VertexArrayId);
        ShaderTestFramework.SetUniform(layout.GetUniformLocation(program.ProgramId, "vge_atmosphereDisk"), 3f, 2f, 1f, AtmosphereSolarDisk.AngularRadius);
        GL.Enable(EnableCap.DepthTest); GL.DepthFunc(DepthFunction.Less); GL.DepthMask(true);
        GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.CullFace);
        float[]? baseline = null;
        for (int iteration = 0; iteration < 4; iteration++)
        {
            target.BindWithViewport();
            GL.ClearColor(0, 0, 0, 0); GL.ClearDepth(iteration == 3 ? .5 : 1);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            ShaderTestFramework.SetUniform(layout.GetUniformLocation(program.ProgramId, "camera"), iteration * 37f, iteration * -19f, iteration * 123f);
            ShaderTestFramework.SetUniform(layout.GetUniformLocation(program.ProgramId, "vge_atmosphereSun"), 0f, 0f, -1f, iteration == 2 ? 0f : -1f);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
            float[] pixels = target[0].ReadPixels();
            if (iteration == 0)
            {
                baseline = pixels;
                float expected = displayTransfer ? 1.055f * MathF.Pow(.75f, 1 / 2.4f) - .055f : 3f;
                Assert.InRange(pixels[(32 * 64 + 32) * 4], expected - .00001f, expected + .00001f);
                Assert.Equal(0f, pixels[0]);
            }
            else if (iteration == 1) Assert.Equal(baseline!, pixels);
            else if (iteration == 3) Assert.All(pixels, value => Assert.Equal(0f, value));
            else
            {
                float full = 0, half = 0;
                for (int i = 3; i < pixels.Length; i += 4) { full += baseline![i]; half += pixels[i]; }
                Assert.InRange(half / full, .47f, .53f);
            }
        }
    }
    #endregion
}

