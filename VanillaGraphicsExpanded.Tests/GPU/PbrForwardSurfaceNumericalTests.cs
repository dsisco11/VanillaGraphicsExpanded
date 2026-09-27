using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks shared forward lighting at shadow, emission and view-space point-light boundaries.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class PbrForwardSurfaceNumericalTests : RenderTestBase
{
    /// <summary>Uses the shared headless context.</summary>
    public PbrForwardSurfaceNumericalTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Numerical ownership
    /// <summary>Complete occlusion removes sun, emission occurs once, and rotating the view preserves point illumination.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void ForwardBoundaryMatchesIndependentRadiance(int scenario)
    {
        EnsureContextValid();
        string directory = Path.Combine(AppContext.BaseDirectory, "assets/shaders/includes");
        string common = File.ReadAllText(Path.Combine(directory, "pbr_common.glsl"))
            .Replace("@import \"./common_constants.glsl\"", File.ReadAllText(Path.Combine(directory, "common_constants.glsl")));
        string header = """
            #version 330 core
            #define SHADOWQUALITY 1
            #define DYNLIGHTS 1
            #define VGE_SURFACE_VIEW surfaceView
            uniform mat4 surfaceView;
            uniform sampler2DShadow shadowMapFar;
            vec4 shadowCoordsFar = vec4(0.5, 0.5, 0.8, 1.0);
            float shadowIntensity = 1.0;
            vec3 lightPosition = vec3(0,0,1);
            vec3 vge_viewPosition = vec3(0,0,-2);
            vec3 vge_blockIrradiance = vec3(0);
            vec3 vge_sunIrradiance = vec3(0.2,0.4,0.8);
            vec4 rgbaFog = vec4(0);
            layout(location=0) out vec4 outColor;
            """;
        if (scenario == 5) header = header.Replace("vec3(0.2,0.4,0.8)", "vec3(0.0)");
        string source = header + "\n" + File.ReadAllText(Path.Combine(directory, "pbr_color.glsl")) + "\n" + common + "\n"
            + File.ReadAllText(Path.Combine(directory, "pbr_direct_brdf.glsl")) + "\n"
            + File.ReadAllText(Path.Combine(directory, "pbr_forward_surface.glsl"))
            + $"\nvoid main() {{ outColor = vec4(VgeForwardSurface(vec3(0.2,0.4,0.6), {(scenario == 3 ? "vec3(1,0,0)" : "vec3(0,0,1)")}, vec3(0.5,0,{(scenario == 1 ? "2.0" : "0.0")}), 0.0), 0.37); }}";
        int vertex = Compile(ShaderType.VertexShader, "#version 330 core\nlayout(location=0) in vec2 position; void main(){gl_Position=vec4(position,0,1);}");
        int fragment = Compile(ShaderType.FragmentShader, source);
        int program = GL.CreateProgram();
        try
        {
            GL.AttachShader(program, vertex); GL.AttachShader(program, fragment); GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.True(linked != 0, GL.GetProgramInfoLog(program));
            GL.UseProgram(program);
            float[] view = scenario == 3 ? [0,0,1,0, 0,1,0,0, -1,0,0,0, 0,0,0,1] : [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1];
            GL.UniformMatrix4(GL.GetUniformLocation(program, "surfaceView"), 1, false, view);
            GL.Uniform1(GL.GetUniformLocation(program, "pointLightQuantity"), scenario is 2 or 3 ? 1 : 0);
            GL.Uniform3(GL.GetUniformLocation(program, "pointLights[0]"), 0f, 0f, 0f);
            GL.Uniform3(GL.GetUniformLocation(program, "pointLightColors[0]"), 1f, 1f, 1f);
            GL.Uniform1(GL.GetUniformLocation(program, "shadowMapFar"), 0);
            using var framework = new ShaderTestFramework();
            using var depth = Texture2D.Create(1, 1, PixelInternalFormat.DepthComponent32f);
            // The generic float uploader assumes color channels; explicitly initialize the depth comparison input.
            GL.ClearTexImage(depth.TextureId, 0, PixelFormat.DepthComponent, PixelType.Float, new[] { scenario >= 4 ? 1f : 0f });
            using var output = framework.CreateTestGBuffer(1, 1, PixelInternalFormat.Rgba32f);
            depth.Bind(0);
            GpuSamplers.ShadowCompareLinearClamp.Bind(0);
            framework.RenderQuadTo(program, output);
            float[] actual = output[0].ReadPixels();
            float factor = scenario == 1 ? 2f : scenario is 2 or 3 ? 0.25f : 0f;
            for (int channel = 0; channel < 3; channel++)
            {
                float irradiance = scenario == 4 ? new[] { .2f, .4f, .8f }[channel] : factor;
                float linear = (channel + 1) * .2f * irradiance;
                float mapped = linear / (1f + linear);
                float expected = mapped <= .0031308f ? 12.92f * mapped : 1.055f * MathF.Pow(mapped, 1f / 2.4f) - .055f;
                Assert.InRange(actual[channel], expected - .0001f, expected + .0001f);
            }
            Assert.InRange(actual[3], .36999f, .37001f);
        }
        finally { GL.DeleteProgram(program); GL.DeleteShader(vertex); GL.DeleteShader(fragment); }
    }
    #endregion

    #region Driver compilation
    /// <summary>Compiles production helper text at the engine runtime GLSL boundary.</summary>
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

