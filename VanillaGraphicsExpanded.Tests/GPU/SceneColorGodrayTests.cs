using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks spatial solar rays against the installed artistic algorithm and its scene-linear boundary.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class SceneColorGodrayTests(HeadlessGLFixture fixture, ITestOutputHelper output) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Nonzero traversal preserves a graded footprint without spreading unbounded physical solar radiance.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void SolarTraversalMatchesBoundedLegacyAlgorithm(int linearScene)
    {
        EnsureContextValid();
        const int size = 64;
        using var shaders = new TerrainShaderTestFixture();
        string game = Environment.GetEnvironmentVariable("VINTAGE_STORY")!;
        string installed = File.ReadAllText(Path.Combine(game, "assets/game/shaders/godrays.fsh"));
        using var actualProgram = Link(shaders, PbrSurfaceInstalledShaderTests.Build("godrays.fsh", 1, 0, 0, 0, 0));
        using var referenceProgram = Link(shaders, installed);
        using var framework = new ShaderTestFramework();
        var hdr = new float[size * size * 4];
        var display = new float[hdr.Length];
        var glow = new float[hdr.Length];
        float diskDisplay = Encode(1000f / 1001f);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int offset = (y * size + x) * 4;
            bool disk = (x + .5f - size / 2) * (x + .5f - size / 2)
                + (y + .5f - size / 2) * (y + .5f - size / 2) <= 9;
            for (int channel = 0; channel < 3; channel++)
            {
                hdr[offset + channel] = disk ? 1000 : 0;
                display[offset + channel] = disk ? diskDisplay : 0;
            }
            hdr[offset + 3] = display[offset + 3] = glow[offset + 3] = 1;
            glow[offset + 1] = disk ? 1 : 0;
        }
        using var hdrTexture = framework.CreateTexture(size, size, PixelInternalFormat.Rgba32f, hdr);
        using var displayTexture = framework.CreateTexture(size, size, PixelInternalFormat.Rgba32f, display);
        using var glowTexture = framework.CreateTexture(size, size, PixelInternalFormat.Rgba32f, glow);
        using var target = CreateMRTRenderTarget(size, size, PixelInternalFormat.Rgba32f);
        float[] reference = Draw(referenceProgram, displayTexture.TextureId);
        GL.ProgramUniform1(actualProgram.ProgramId, GL.GetUniformLocation(actualProgram.ProgramId, "vge_sceneLinear"), linearScene);
        float[] actual = Draw(actualProgram, linearScene == 0 ? displayTexture.TextureId : hdrTexture.TextureId);
        int gradedOutside = 0;
        var levels = new HashSet<int>();
        float peak = 0;
        for (int pixel = 0; pixel < size * size; pixel++)
        {
            float expected = linearScene == 0 ? reference[pixel * 4] : Decode(reference[pixel * 4]);
            float value = actual[pixel * 4];
            Assert.True(float.IsFinite(value));
            Assert.InRange(value, expected - .0001f, expected + .0001f);
            Assert.InRange(value, 0, 1);
            Assert.Equal(1, actual[pixel * 4 + 3]);
            peak = Math.Max(peak, value);
            if (hdr[pixel * 4] == 0 && value > .0001f)
            {
                gradedOutside++;
                levels.Add((int)(value * 10000));
            }
        }
        Assert.True(gradedOutside > 20);
        Assert.True(levels.Count > 5);
        output.WriteLine($"mode={linearScene}, peak={peak}, lit outside pixels={gradedOutside}, distinct halo levels={levels.Count}");
        if (linearScene != 0)
        {
            // Reproduce the captured defect independently: a display metric controls
            // suppression while the artistic accumulator still transports HDR RGB.
            string helper = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets/shaders/includes/pbr_color.glsl"));
            string old = installed.Replace("#version 330 core", "#version 330 core\n" + helper)
                .Replace("(col.r+col.g+col.b)/3", "dot(VgeResolveDisplay(col.rgb),vec3(1.0/3.0))");
            using var oldProgram = Link(shaders, old);
            float[] oldPixels = Draw(oldProgram, hdrTexture.TextureId);
            float oldPeak = oldPixels.Where((_, index) => index % 4 == 0).Max();
            Assert.True(oldPeak > 100);
            output.WriteLine($"old HDR accumulator peak={oldPeak}");
        }
        AssertNoGLError("spatial solar godray traversal");

        // Every readback releases its temporary framebuffer; RenderQuadTo restores
        // the intended target and viewport before each independent draw.
        /// <summary>Restores stage inputs and output for one complete spatial draw and readback.</summary>
        float[] Draw(GpuProgramObject program, int texture)
        {
            GL.ProgramUniform1(program.ProgramId, GL.GetUniformLocation(program.ProgramId, "inputTexture"), 0);
            GL.ProgramUniform1(program.ProgramId, GL.GetUniformLocation(program.ProgramId, "glowParts"), 1);
            using var input = StateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, texture);
            using var mask = StateCache.Current.BindTextureScope(TextureTarget.Texture2D, 1, glowTexture.TextureId);
            framework.RenderQuadTo(program.ProgramId, target);
            return target[0].ReadPixels();
        }
    }
    #endregion

    #region Private
    /// <summary>Supplies a spatial fullscreen interface with nonzero ray intensity and traversal direction.</summary>
    private static GpuProgramObject Link(TerrainShaderTestFixture shaders, string fragment)
    {
        int vertex = shaders.Compile(ShaderType.VertexShader, """
            #version 430 core
            layout(location=0) in vec2 position;
            out vec2 texCoord; out vec3 sunPosScreen; out float iGlobalTime; out float intensity; out float direction;
            void main(){gl_Position=vec4(position,0,1);texCoord=position*.5+.5;sunPosScreen=vec3(0);iGlobalTime=0;intensity=.7;direction=1;}
            """);
        return GpuProgramObject.Adopt(TerrainShaderTestFixture.Link(vertex, shaders.Compile(ShaderType.FragmentShader, fragment)));
    }

    /// <summary>Decodes the reference artistic output independently of production GLSL.</summary>
    private static float Decode(float value) => value <= .04045f ? value / 12.92f : MathF.Pow((value + .055f) / 1.055f, 2.4f);
    /// <summary>Encodes a resolved solar sample independently of production GLSL.</summary>
    private static float Encode(float value) => value <= .0031308f ? value * 12.92f : 1.055f * MathF.Pow(value, 1f / 2.4f) - .055f;
    #endregion
}
