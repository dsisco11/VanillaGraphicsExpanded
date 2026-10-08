using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises physical solar radiance through the installed extraction, blur and patched display stages.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class AtmosphereSunBloomPipelineTests(HeadlessGLFixture fixture, ITestOutputHelper testOutput) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Default atmospheric solar radiance produces a finite visible halo after the complete HDR bloom chain.</summary>
    [Theory]
    [InlineData(.1f)]
    [InlineData(1f)]
    public void PhysicalSolarDiskRetainsBloomThroughFinal(float ambientBloom)
    {
        EnsureContextValid();
        const int size = 64;
        using var stages = new TerrainShaderTestFixture();
        string game = Environment.GetEnvironmentVariable("VINTAGE_STORY")!;
        string includes = Path.Combine(AppContext.BaseDirectory, "assets/shaders/includes");
        int solarVertex = stages.Compile(ShaderType.VertexShader, """
            #version 430 core
            out vec3 vge_sunDirection;
            out vec2 vge_sunPlane;
            void main() {
                vec2 p[3]=vec2[3](vec2(-1,-1),vec2(3,-1),vec2(-1,3));
                gl_Position=vec4(p[gl_VertexID],0,1);
                vge_sunDirection=vec3(0,1,0);
                vge_sunPlane=p[gl_VertexID]*(64.0/6.0);
            }
            """);
        int solarFragment = stages.Compile(ShaderType.FragmentShader, """
            #version 430 core
            uniform int vge_pbrRoute;
            #define VGE_SURFACE_PRIMARY_OUTPUTS 0
            #define SSAOLEVEL 0
            layout(location=0) out vec4 outColor;
            layout(location=1) out vec4 outGlow;
            const float extraGodray=0;
            float getSkyMurkiness() { return 0; }
            vec3 applyUnderwaterEffects(vec3 color,float murk) { return color; }
            """ + "\n" + File.ReadAllText(Path.Combine(includes, "pbr_color.glsl")) + "\n"
            + File.ReadAllText(Path.Combine(includes, "atmosphere_sun_fragment.glsl"))
            + "\nvoid main() { VgeDrawAtmosphericSun(); }");
        using var solar = GpuProgramObject.Adopt(TerrainShaderTestFixture.Link(solarVertex, solarFragment));
        using var bright = LinkInstalled(stages, game, "findbright", false);
        using var blur = LinkInstalled(stages, game, "blur", true);
        using var final = LinkInstalled(stages, game, "final", true);
        using var vao = GpuVao.Create();
        using var color = DynamicTexture2D.Create(size, size, PixelInternalFormat.Rgba16f);
        using var glow = DynamicTexture2D.Create(size, size, PixelInternalFormat.Rgba8);
        using var source = GpuFramebuffer.CreateMRT([color, glow])!;
        using var framework = new ShaderTestFramework();
        using var extracted = framework.CreateTestGBuffer(size, size, PixelInternalFormat.Rgba16f);
        using var horizontal = framework.CreateTestGBuffer(size, size, PixelInternalFormat.Rgba16f);
        using var vertical = framework.CreateTestGBuffer(size, size, PixelInternalFormat.Rgba16f);
        using var dark = framework.CreateTestGBuffer(size, size, PixelInternalFormat.Rgba16f);
        using var output = framework.CreateTestGBuffer(size, size, PixelInternalFormat.Rgba8);
        StateCache.Current.BindVertexArray(vao.VertexArrayId);
        GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.CullFace); GL.Disable(EnableCap.FramebufferSrgb);
        GL.Disable(EnableCap.Blend);
        dark.BindWithViewport(); GL.ClearColor(0, 0, 0, 0); GL.Clear(ClearBufferMask.ColorBufferBit);
        source.BindWithViewport();
        GL.ClearBuffer(ClearBuffer.Color, 0, new[] { .05f, .05f, .05f, 1f });
        GL.ClearBuffer(ClearBuffer.Color, 1, new[] { 0f, 0f, 0f, 0f });
        var lighting = new AtmosphereLighting(Vector3.UnitY, AtmosphereModel.SolarIrradiance(Vector3.UnitY, .001f, 1f),
            Vector3.Zero, Vector3.Zero, Vector3.Zero, []) { HorizonElevation = AtmosphereSkyMapping.Horizon(.001f) };
        Vector3 radiance = AtmosphereSolarDisk.Radiance(lighting);
        Assert.True(radiance.X > 100 && radiance.Y > 100 && radiance.Z > 100);
        StateCache.Current.UseProgram(solar.ProgramId);
        Uniform(solar, "vge_pbrRoute", 1);
        GL.Uniform4(GL.GetUniformLocation(solar.ProgramId, "vge_atmosphereDisk"), radiance.X, radiance.Y, radiance.Z, AtmosphereSolarDisk.AngularRadius);
        GL.Uniform4(GL.GetUniformLocation(solar.ProgramId, "vge_atmosphereSun"), 0f, 1f, 0f, lighting.HorizonElevation);
        GL.Enable(EnableCap.Blend); GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3); GL.Disable(EnableCap.Blend);
        extracted.BindWithViewport(); StateCache.Current.UseProgram(bright.ProgramId);
        Texture(bright, "colorTex", 0, color.TextureId); Texture(bright, "glowTex", 1, glow.TextureId);
        GL.Uniform1(GL.GetUniformLocation(bright.ProgramId, "ambientBloomLevel"), ambientBloom);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        horizontal.BindWithViewport(); StateCache.Current.UseProgram(blur.ProgramId);
        Texture(blur, "inputTexture", 0, extracted[0].TextureId);
        GL.Uniform2(GL.GetUniformLocation(blur.ProgramId, "frameSize"), (float)size, size);
        Uniform(blur, "isVertical", 0); GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        vertical.BindWithViewport(); Texture(blur, "inputTexture", 0, horizontal[0].TextureId);
        Uniform(blur, "isVertical", 1); GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        var blurred = vertical[0].ReadPixels();
        Assert.All(blurred, value => Assert.True(float.IsFinite(value) && value >= 0));
        int halo = ((size / 2) * size + size / 2 + 5) * 4;
        Assert.True(blurred[halo] > 0);
        output.BindWithViewport(); StateCache.Current.UseProgram(final.ProgramId);
        Texture(final, "primaryScene", 0, color.TextureId);
        Texture(final, "bloomParts", 2, dark[0].TextureId);
        Uniform(final, "vge_sceneLinear", 1);
        GL.Uniform2(GL.GetUniformLocation(final.ProgramId, "invFrameSizeIn"), 1f / size, 1f / size);
        GL.Uniform1(GL.GetUniformLocation(final.ProgramId, "gammaLevel"), 1f);
        GL.Uniform1(GL.GetUniformLocation(final.ProgramId, "brightnessLevel"), 1f);
        GL.Uniform1(GL.GetUniformLocation(final.ProgramId, "ambientBloomLevel"), ambientBloom);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        float baseline = output[0].ReadPixels()[halo];
        // Readback temporarily owns and then unbinds its framebuffer. Restore the draw target.
        output.BindWithViewport();
        Texture(final, "bloomParts", 2, vertical[0].TextureId);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        float withBloom = output[0].ReadPixels()[halo];
        testOutput.WriteLine($"ambient={ambientBloom}, radiance={radiance}, blurred halo={blurred[halo]}, display halo={withBloom}, zero-bloom baseline={baseline}");
        Assert.True(withBloom > baseline + 1f / 255f, $"radiance={radiance}, halo={blurred[halo]}, final={withBloom}, baseline={baseline}");
        AssertNoGLError("physical sun bloom pipeline");
    }
    #endregion

    #region Private
    /// <summary>Links installed stages, applying production patch expansion to scene-aware stages.</summary>
    private static GpuProgramObject LinkInstalled(TerrainShaderTestFixture stages, string game, string name, bool patched)
    {
        string vertex = File.ReadAllText(Path.Combine(game, "assets/game/shaders", name + ".vsh"));
        string fragment = patched ? PbrSurfaceInstalledShaderTests.Build(name + ".fsh", 0, 0, 0, 0, 0)
            : File.ReadAllText(Path.Combine(game, "assets/game/shaders", name + ".fsh"));
        if (name == "final") fragment = fragment.Replace("#version 330 core", "#version 330 core\n#define BLOOM 1\n#define GODRAYS 0\n#define FXAA 0\n");
        return GpuProgramObject.Adopt(TerrainShaderTestFixture.Link(stages.Compile(ShaderType.VertexShader, vertex), stages.Compile(ShaderType.FragmentShader, fragment)));
    }

    /// <summary>Assigns the linked integer input for this draw.</summary>
    private static void Uniform(GpuProgramObject program, string name, int value)
    {
        int location = GL.GetUniformLocation(program.ProgramId, name);
        Assert.True(location >= 0, $"Expected active input {name}");
        GL.Uniform1(location, value);
        GL.GetUniform(program.ProgramId, location, out int actual);
        Assert.Equal(value, actual);
    }

    /// <summary>Binds a concrete scene image and assigns its sampler for the current stage.</summary>
    private static void Texture(GpuProgramObject program, string name, int unit, int texture)
    {
        StateCache.Current.BindTexture(TextureTarget.Texture2D, unit, texture);
        Uniform(program, name, unit);
    }
    #endregion
}
