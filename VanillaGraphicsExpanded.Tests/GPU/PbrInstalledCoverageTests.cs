using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Executes installed engine fragment shaders to protect opaque and OIT coverage.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class PbrInstalledCoverageTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Coverage preservation
    /// <summary>Authored alpha survives material interception and reaches engine OIT revealage unchanged.</summary>
    [Theory]
    [InlineData("standard", 0, 1, 1f)]
    [InlineData("standard", 0, 2, 1f)]
    [InlineData("standard", 0, 2, .25f)]
    [InlineData("entityanimated", 0, 1, 1f)]
    [InlineData("entityanimated", 0, 2, 1f)]
    [InlineData("entityanimated", 0, 2, .25f)]
    [InlineData("entityanimated", 1, 1, 1f)]
    [InlineData("entityanimated", 1, 2, 1f)]
    [InlineData("entityanimated", 1, 2, .25f)]
    public void InstalledFragmentPreservesCoverage(string family, int oit, int route, float alpha)
    {
        EnsureContextValid();
        using var shaders = new TerrainShaderTestFixture();
        int vertex = shaders.Compile(ShaderType.VertexShader, """
            #version 430 core
            out vec2 uv; out vec4 color; out vec4 rgbaFog; out float fogAmount; out float glowLevel;
            out vec3 vertexPosition; flat out int renderFlags; out vec3 normal; out vec4 worldPos;
            out vec3 blockLight; out vec4 camPos; out float damageEffect; out float fragFrostAlpha;
            out vec4 rgbaGlow; out vec3 vge_viewPosition; out vec3 vge_blockIrradiance;
            out vec3 vge_sunIrradiance; out float vge_skyVisibility;
            void main() {
                vec2 p[3]=vec2[3](vec2(-1,-1),vec2(3,-1),vec2(-1,3)); gl_Position=vec4(p[gl_VertexID],0,1);
                uv=vec2(.5); color=vec4(1); rgbaFog=vec4(0); fogAmount=0; glowLevel=0;
                vertexPosition=vec3(0); renderFlags=0; normal=vec3(0,0,1); worldPos=vec4(0,0,-2,1);
                blockLight=vec3(1); camPos=worldPos; damageEffect=0; fragFrostAlpha=0; rgbaGlow=vec4(0);
                vge_viewPosition=vec3(0,0,-2); vge_blockIrradiance=vec3(1); vge_sunIrradiance=vec3(0); vge_skyVisibility=0;
            }
            """);
        int fragment = shaders.Compile(ShaderType.FragmentShader,
            PbrSurfaceInstalledShaderTests.Build(family + ".fsh", 0, oit, 0, 0, 0));
        using var program = GpuProgramObject.Adopt(TerrainShaderTestFixture.Link(vertex, fragment));
        using var vao = GpuVao.Create();
        using var framework = new ShaderTestFramework();
        using var target = framework.CreateTestGBuffer(1, 1, PixelInternalFormat.Rgba32f, oit > 0 ? 6 : 2);
        using var texture = framework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, new[] { .2f, .4f, .6f, alpha });
        var layout = GpuProgramLayout.TryBuild(program.ProgramId);
        GlStateCache.Current.UseProgram(program.ProgramId); GlStateCache.Current.BindVertexArray(vao.VertexArrayId);
        texture.Bind(0);
        ShaderTestFramework.SetUniform(layout.GetUniformLocation(program.ProgramId, family == "standard" ? "tex" : "entityTex"), 0);
        ShaderTestFramework.SetUniform(layout.GetUniformLocation(program.ProgramId, "vge_pbrRoute"), route);
        ShaderTestFramework.SetUniform(layout.GetUniformLocation(program.ProgramId, "vge_atmosphereAerialRadiance"), 11);
        ShaderTestFramework.SetUniform(layout.GetUniformLocation(program.ProgramId, "vge_atmosphereAerialAttenuation"), 12);
        GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.CullFace);
        target.BindWithViewport(); GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        float[] actual = target[oit > 0 ? 1 : 0].ReadPixels();
        float expected = oit > 0 ? 1 - alpha : alpha;
        Assert.InRange(actual[3], expected - 1e-6f, expected + 1e-6f);
        if (oit > 0)
        {
            float[] accumulation = target[3].ReadPixels();
            Assert.InRange(accumulation[3], alpha - 1e-6f, alpha + 1e-6f);
        }
    }
    #endregion
}
