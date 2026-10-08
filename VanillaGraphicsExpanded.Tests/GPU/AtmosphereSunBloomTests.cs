using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks solar visibility metadata and the retained offscreen bloom handoff.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class AtmosphereSunBloomTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Bloom extraction
    /// <summary>HDR disks publish bounded visibility while offscreen disks retain authored engine bloom metadata.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void SolarMetadataSeparatesOwnedAndOffscreenBloom(int route)
    {
        EnsureContextValid();
        using var shaders = new TerrainShaderTestFixture();
        int vertex = shaders.Compile(ShaderType.VertexShader, """
            #version 430 core
            out vec2 texcoord;
            out vec3 vge_sunDirection;
            out vec2 vge_sunPlane;
            void main() {
                vec2 p[3]=vec2[3](vec2(-1,-1),vec2(3,-1),vec2(-1,3));
                gl_Position=vec4(p[gl_VertexID],0,1);
                texcoord=p[gl_VertexID]*.5+.5;
                vge_sunDirection=vec3(0,1,0);
                vge_sunPlane=vec2(0);
            }
            """);
        string includes = Path.Combine(AppContext.BaseDirectory, "assets/shaders/includes");
        int sunFragment = shaders.Compile(ShaderType.FragmentShader, """
            #version 430 core
            uniform int vge_pbrRoute;
            #define VGE_SURFACE_PRIMARY_OUTPUTS 0
            #define SSAOLEVEL 0
            layout(location=0) out vec4 outColor;
            layout(location=1) out vec4 outGlow;
            layout(std140) uniform TestInputs { float attenuation; };
            const float extraGodray=.7;
            float getSkyMurkiness() { return 0; }
            vec3 applyUnderwaterEffects(vec3 color,float murk) { return color*attenuation; }
            """ + "\n" + File.ReadAllText(Path.Combine(includes, "pbr_color.glsl")) + "\n"
            + File.ReadAllText(Path.Combine(includes, "atmosphere_sun_fragment.glsl"))
            + "\nvoid main() { VgeDrawAtmosphericSun(); }");
        string game = Environment.GetEnvironmentVariable("VINTAGE_STORY")!;
        int bloomFragment = shaders.Compile(ShaderType.FragmentShader,
            File.ReadAllText(Path.Combine(game, "assets/game/shaders/findbright.fsh")));
        using var sun = GpuProgramObject.Adopt(TerrainShaderTestFixture.Link(vertex, sunFragment));
        using var bloom = GpuProgramObject.Adopt(TerrainShaderTestFixture.Link(vertex, bloomFragment));
        using var vao = GpuVao.Create();
        using var color = DynamicTexture2D.Create(1, 1, PixelInternalFormat.Rgba32f);
        using var glow = DynamicTexture2D.Create(1, 1, PixelInternalFormat.Rgba32f);
        using var source = GpuFramebuffer.CreateMRT([color, glow])!;
        using var framework = new ShaderTestFramework();
        using var output = framework.CreateTestGBuffer(1, 1, PixelInternalFormat.Rgba32f);
        using var inputs = new FixtureUniformInputs(sun.ProgramId, 16);
        var sunLayout = GpuProgramLayout.TryBuild(sun.ProgramId);
        var bloomLayout = GpuProgramLayout.TryBuild(bloom.ProgramId);
        StateCache.Current.BindVertexArray(vao.VertexArrayId);
        GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.CullFace);
        float previous = float.PositiveInfinity;
        // Bright, attenuated underwater, dim, and fully extinguished solar inputs.
        foreach (var (radiance, attenuation) in new[] { (100f, 1f), (100f, .5f), (.01f, 1f), (0f, 1f) })
        {
            source.BindWithViewport();
            GL.ClearColor(0, 0, 0, 0); GL.Clear(ClearBufferMask.ColorBufferBit);
            StateCache.Current.UseProgram(sun.ProgramId);
            GL.Uniform1(GL.GetUniformLocation(sun.ProgramId, "vge_pbrRoute"), route);
            ShaderTestFramework.SetUniform(sunLayout.GetUniformLocation(sun.ProgramId, "vge_atmosphereDisk"), radiance, radiance, radiance, .01f);
            ShaderTestFramework.SetUniform(sunLayout.GetUniformLocation(sun.ProgramId, "vge_atmosphereSun"), 0f, 1f, 0f, 0f);
            inputs.Float(0, attenuation);
            inputs.Publish();
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            float[] solar = source[0].ReadPixels();
            float[] emission = source[1].ReadPixels();
            if (radiance > 0)
            {
                float mapped = 1.055f * MathF.Pow(radiance / (1 + radiance), 1 / 2.4f) - .055f;
                float expectedMarker = route == 0 ? mapped * attenuation : 0;
                Assert.InRange(emission[0], expectedMarker - 1e-6f, expectedMarker + 1e-6f);
                Assert.Equal(route == 0 ? .7f : 1f, emission[1]);
                Assert.Equal(1f, emission[3]);
            }
            else Assert.All(emission, value => Assert.Equal(0f, value));
            output.BindWithViewport();
            StateCache.Current.UseProgram(bloom.ProgramId);
            using var colorBinding = StateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, color.TextureId);
            using var glowBinding = StateCache.Current.BindTextureScope(TextureTarget.Texture2D, 1, glow.TextureId);
            ShaderTestFramework.SetUniform(bloomLayout.GetUniformLocation(bloom.ProgramId, "colorTex"), 0);
            ShaderTestFramework.SetUniform(bloomLayout.GetUniformLocation(bloom.ProgramId, "glowTex"), 1);
            ShaderTestFramework.SetUniform(bloomLayout.GetUniformLocation(bloom.ProgramId, "ambientBloomLevel"), 0f);
            ShaderTestFramework.SetUniform(bloomLayout.GetUniformLocation(bloom.ProgramId, "extraBloom"), 0f);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            float actual = output[0].ReadPixels()[0];
            Assert.InRange(actual, solar[0] * 3 * emission[0] - 1e-6f, solar[0] * 3 * emission[0] + 1e-6f);
            if (route == 0)
            {
                Assert.True(actual < previous);
                if (radiance > 0) Assert.True(actual > 0);
                else Assert.Equal(0f, actual);
            }
            else Assert.Equal(0f, actual);
            previous = actual;
        }
    }
    #endregion
}
