using System.Collections.Immutable;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Executes finite-path lookup against independent angular and distance ramps.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class AtmosphereAerialLookupTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Lookup reconstruction
    /// <summary>Physical distance, angular direction, identity and enclosure gates select the intended volume coordinates.</summary>
    [Fact]
    public void SamplesAngularDistanceGridAndPreservesIdentity()
    {
        EnsureContextValid();
        using var shaders = new TerrainShaderTestFixture();
        int vertex = shaders.Compile(ShaderType.VertexShader, """
            #version 430 core
            void main() { vec2 p[3]=vec2[3](vec2(-1,-1),vec2(3,-1),vec2(-1,3)); gl_Position=vec4(p[gl_VertexID],0,1); }
            """);
        string directory = Path.Combine(AppContext.BaseDirectory, "assets/shaders/includes");
        string source = File.ReadAllText(Path.Combine(directory, "atmosphere_aerial.glsl"))
            .Replace("@import \"./atmosphere_sky_mapping.glsl\"", File.ReadAllText(Path.Combine(directory, "atmosphere_sky_mapping.glsl")))
            .Replace("@import \"./atmosphere_aerial_mapping.glsl\"", File.ReadAllText(Path.Combine(directory, "atmosphere_aerial_mapping.glsl")));
        int fragment = shaders.Compile(ShaderType.FragmentShader, "#version 430 core\n" + source + """

            uniform vec3 displacement;
            uniform float visibility;
            layout(location=0) out vec4 result;
            void main() { result=vec4(VgeApplyAerial(vec3(1), displacement, visibility, vec2(.001,0)),1); }
            """);
        using var program = GpuProgramObject.Adopt(TerrainShaderTestFixture.Link(vertex, fragment));
        using var vao = GpuVao.Create();
        using var framework = new ShaderTestFramework();
        using var target = framework.CreateTestGBuffer(1, 1, PixelInternalFormat.Rgba32f);
        using var owner = new AtmosphereModSystem();
        const int width = 4, height = 5, depth = 24;
        float[] scatter = new float[width * height * depth * 4], loss = new float[scatter.Length];
        for (int z = 0; z < depth; z++)
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int offset = ((z * height + y) * width + x) * 4;
            scatter[offset] = (float)z / (depth - 1); scatter[offset + 1] = (float)y / (height - 1);
            scatter[offset + 2] = (float)x / width; scatter[offset + 3] = loss[offset + 3] = 1;
        }
        owner.Publish(new(Vector3.UnitY, Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero,
            ImmutableArray.CreateRange(new float[width * height * 4]))
        { Width = width, Height = height, AerialRadiance = ImmutableArray.CreateRange(scatter), AerialAttenuation = ImmutableArray.CreateRange(loss) });
        var layout = GpuProgramLayout.TryBuild(program.ProgramId);
        GlStateCache.Current.UseProgram(program.ProgramId); GlStateCache.Current.BindVertexArray(vao.VertexArrayId);
        using var r = GlStateCache.Current.BindTextureScope(TextureTarget.Texture3D, 11, AtmosphereModSystem.AerialRadianceTextureId);
        using var a = GlStateCache.Current.BindTextureScope(TextureTarget.Texture3D, 12, AtmosphereModSystem.AerialAttenuationTextureId);
        ShaderTestFramework.SetUniform(layout.GetUniformLocation(program.ProgramId, "vge_atmosphereAerialRadiance"), 11);
        ShaderTestFramework.SetUniform(layout.GetUniformLocation(program.ProgramId, "vge_atmosphereAerialAttenuation"), 12);
        GlStateCache.Current.UnbindSampler(11); GlStateCache.Current.UnbindSampler(12);
        GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.CullFace);
        foreach (float row in new[] { .25f, .5f, .75f })
        foreach (float slice in new[] { .2f, .8f })
        foreach (float visibility in new[] { 0f, 1f })
        foreach (float azimuth in new[] { 0f, MathF.PI / 2 })
        {
            float elevation = AtmosphereSkyMapping.Elevation(row, 0);
            float distance = MathF.Exp(MathF.Log(2500001f) * slice) - 1;
            Vector3 direction = new(MathF.Cos(elevation) * MathF.Cos(azimuth), MathF.Sin(elevation), MathF.Cos(elevation) * MathF.Sin(azimuth));
            target.BindWithViewport();
            ShaderTestFramework.SetUniform(layout.GetUniformLocation(program.ProgramId, "displacement"), direction.X * distance, direction.Y * distance, direction.Z * distance);
            ShaderTestFramework.SetUniform(layout.GetUniformLocation(program.ProgramId, "visibility"), visibility);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            float[] actual = target[0].ReadPixels();
            Assert.InRange(MathF.Abs(actual[0] - (1 + slice * visibility)), 0, .001f);
            Assert.InRange(MathF.Abs(actual[1] - (1 + row * visibility)), 0, .001f);
            Assert.InRange(MathF.Abs(actual[2] - (1 + (azimuth == 0 ? .375f : .125f) * visibility)), 0, .001f);
        }
        target.BindWithViewport();
        ShaderTestFramework.SetUniform(layout.GetUniformLocation(program.ProgramId, "displacement"), 0f, 0f, 0f);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        Assert.Equal(new[] { 1f, 1f, 1f, 1f }, target[0].ReadPixels());
    }
    #endregion
}
