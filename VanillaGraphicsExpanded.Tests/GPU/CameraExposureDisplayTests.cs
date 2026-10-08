using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises exposure at the installed final and perceptual-luma boundaries.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class CameraExposureDisplayTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Manual and published exposure affect display brightness while preserving scene samples and alpha contracts.</summary>
    [Theory]
    [InlineData("final.fsh", -2f, false, 1)]
    [InlineData("final.fsh", 2f, false, 1)]
    [InlineData("final.fsh", -2f, true, 1)]
    [InlineData("final.fsh", 2f, true, 0)]
    [InlineData("luma.fsh", -2f, false, 1)]
    [InlineData("luma.fsh", 2f, true, 1)]
    [InlineData("luma.fsh", 2f, true, 0)]
    public void InstalledBoundaryUsesExposureOnlyForSceneDisplay(string name, float ev, bool automatic, int linear)
    {
        EnsureContextValid();
        using var shaders = new TerrainShaderTestFixture();
        string source = PbrSurfaceInstalledShaderTests.Build(name, 1, 0, 0, 0, 0)
            .Replace("#version 330 core", "#version 330 core\n#define BLOOM 0\n#define GODRAYS 0\n#define FXAA 0");
        int vertex = shaders.Compile(ShaderType.VertexShader, """
            #version 430 core
            layout(location=0) in vec2 position;
            out vec2 invFrameSize; out vec2 texCoord; out float intensity; out float direction;
            void main(){gl_Position=vec4(position,0,1);invFrameSize=vec2(1);texCoord=vec2(.5);intensity=0;direction=0;}
            """);
        using var program = GpuProgramObject.Adopt(TerrainShaderTestFixture.Link(vertex, shaders.Compile(ShaderType.FragmentShader, source)));
        using var framework = new ShaderTestFramework();
        using var input = framework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, new[] { .18f, .18f, .18f, .37f });
        using var history = framework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, new[] { ev, 0f, 0f, 1f });
        using var target = CreateMRTRenderTarget(1, 1, PixelInternalFormat.Rgba32f);
        foreach (var pair in new[] { ("vge_sceneLinear", linear), ("vge_cameraExposureEnabled", automatic ? 1 : 0), ("vge_cameraExposure", 1), (name == "final.fsh" ? "primaryScene" : "scene", 0) })
            GL.ProgramUniform1(program.ProgramId, GL.GetUniformLocation(program.ProgramId, pair.Item1), pair.Item2);
        GL.ProgramUniform1(program.ProgramId, GL.GetUniformLocation(program.ProgramId, "vge_cameraManualEV"), automatic ? -ev : ev);
        foreach (string uniform in new[] { "gammaLevel", "brightnessLevel" })
            GL.ProgramUniform1(program.ProgramId, GL.GetUniformLocation(program.ProgramId, uniform), 1f);
        using var bindInput = StateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, input.TextureId);
        using var bindHistory = StateCache.Current.BindTextureScope(TextureTarget.Texture2D, 1, history.TextureId);
        framework.RenderQuadTo(program.ProgramId, target);
        float[] actual = target[0].ReadPixels();
        float exposed = .18f * MathF.Pow(2, ev);
        float metric = linear == 0 ? .18f : Encode(exposed / (1 + exposed));
        if (name == "final.fsh")
        {
            float quantized = MathF.Floor(Math.Clamp(metric - .4921875f / 255f, 0, 1) * 255f + .5f) / 255f;
            for (int channel = 0; channel < 3; channel++) Assert.InRange(actual[channel], quantized - .00001f, quantized + .00001f);
            Assert.Equal(1, actual[3]);
        }
        else
        {
            Assert.Equal(new[] { .18f, .18f, .18f }, actual[..3]);
            Assert.InRange(actual[3], metric - .00001f, metric + .00001f);
        }
        Assert.Equal(new[] { .18f, .18f, .18f, .37f }, input.ReadPixels());
    }
    #endregion

    #region Private
    /// <summary>Independently encodes the expected display shoulder result.</summary>
    private static float Encode(float value) => value <= .0031308f ? value * 12.92f : 1.055f * MathF.Pow(value, 1f / 2.4f) - .055f;
    #endregion
}
