using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.SceneColor;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Executes installed sphere fog convex mixing before the production authored-color conversion.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class NativeFogColorEnvelopeTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Actual clamped native sphere weights and one HDR decode retain the current endpoint envelope.</summary>
    [Fact]
    public void InstalledSphereMixFitsPublishedFogEnvelope()
    {
        EnsureContextValid();
        string root = Environment.GetEnvironmentVariable("VINTAGE_STORY") ?? "G:/Vintagestory";
        string source = File.ReadAllText(Path.Combine(root, "assets/game/shaderincludes/fogspheres.ash"));
        int start = source.IndexOf("vec4 applySpheresFog(", StringComparison.Ordinal);
        string function = source[start..];
        string helper = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets/shaders/includes/pbr_color.glsl"));
        using var shaders = new TerrainShaderTestFixture();
        int vertex = shaders.Compile(ShaderType.VertexShader, "#version 330 core\nlayout(location=0)in vec2 position;void main(){gl_Position=vec4(position,0,1);}");
        int fragment = shaders.Compile(ShaderType.FragmentShader, "#version 330 core\nuniform int fogSphereQuantity;uniform float fogSpheres[24];uniform float standardFog;uniform int decode;layout(location=0)out vec4 outColor;\n" +
            helper + function + "\nvoid main(){outColor=applySpheresFog(vec4(1,2,3,1),standardFog,vec3(0,0,1));if(decode>0)outColor.rgb=VgeCompatibilityLuminance(outColor.rgb);}");
        using var program = GpuProgramObject.Adopt(TerrainShaderTestFixture.Link(vertex, fragment));
        using var framework = new ShaderTestFramework();
        using var target = CreateMRTRenderTarget(1, 1, TextureFormat.Rgba32f);
        foreach (float density in new[] { 0f, .25f, 10f })
        foreach (float standardFog in new[] { 0f, .5f, 1f })
        {
            float[] spheres = [0, 0, 0, 2, density, 7, 3, 5, 0, 0, 0, 2, density, 2, 9, 1, 0, 0, 0, 2, density, 4, 1, 8];
            var envelope = SceneColorFogEnvelope.FromInputs(42, new(1, 2, 3), new(0), new(0), 3, spheres);
            GL.ProgramUniform1(program.ProgramId, GL.GetUniformLocation(program.ProgramId, "fogSphereQuantity"), 3);
            GL.ProgramUniform1(program.ProgramId, GL.GetUniformLocation(program.ProgramId, "fogSpheres"), 24, spheres);
            GL.ProgramUniform1(program.ProgramId, GL.GetUniformLocation(program.ProgramId, "standardFog"), standardFog);
            GL.ProgramUniform1(program.ProgramId, GL.GetUniformLocation(program.ProgramId, "decode"), 0);
            framework.RenderQuadTo(program.ProgramId, target); float[] authored = target[0].ReadPixels();
            GL.ProgramUniform1(program.ProgramId, GL.GetUniformLocation(program.ProgramId, "decode"), 1);
            framework.RenderQuadTo(program.ProgramId, target); float[] actual = target[0].ReadPixels();
            for (int channel = 0; channel < 3; channel++)
            {
                double expected = 32 * Math.Pow((authored[channel] + .055) / 1.055, 2.4);
                Assert.InRange(actual[channel], expected * .99999, expected * 1.00001);
                Assert.InRange(actual[channel], 0, envelope.MaximumNits);
            }
            Assert.Equal(authored[3], actual[3]);
        }
    }
    #endregion
}
