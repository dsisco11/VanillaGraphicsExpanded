using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Validates installed bloom blur in destination pixels while retaining HDR energy and all kernel taps.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class SceneColorBloomTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Half and quarter targets retain a symmetric seventeen-tap kernel despite full-resolution engine offsets.</summary>
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void DownsampledBlurUsesDestinationTexels(int divisor)
    {
        EnsureContextValid();
        const int frameWidth = 192, frameHeight = 96;
        int width = frameWidth / divisor, height = frameHeight / divisor;
        int sourceWidth = width * 2, sourceHeight = height * 2;
        using var shaders = new TerrainShaderTestFixture();
        string game = Environment.GetEnvironmentVariable("VINTAGE_STORY")!;
        int vertex = shaders.Compile(ShaderType.VertexShader, File.ReadAllText(Path.Combine(game, "assets/game/shaders/blur.vsh")));
        int fragment = shaders.Compile(ShaderType.FragmentShader, PbrSurfaceInstalledShaderTests.Build("blur.fsh", 0, 0, 0, 0, 0));
        using var program = GpuProgramObject.Adopt(TerrainShaderTestFixture.Link(vertex, fragment));
        using var vao = GpuVao.Create();
        using var framework = new ShaderTestFramework();
        float[] impulse = new float[sourceWidth * sourceHeight * 4];
        // A two-by-two source block represents one destination texel after downsampling.
        // Rectangular viewports ensure pixel spacing cannot be confused with normalized aspect.
        for (int y = height; y < height + 2; y++)
        for (int x = width; x < width + 2; x++)
        for (int channel = 0; channel < 4; channel++) impulse[(y * sourceWidth + x) * 4 + channel] = 1000;
        using var input = framework.CreateTexture(sourceWidth, sourceHeight, PixelInternalFormat.Rgba32f, impulse);
        using var horizontal = framework.CreateTestGBuffer(width, height, PixelInternalFormat.Rgba32f);
        using var vertical = framework.CreateTestGBuffer(width, height, PixelInternalFormat.Rgba32f);
        StateCache.Current.BindVertexArray(vao.VertexArrayId);
        StateCache.Current.UseProgram(program.ProgramId);
        GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.CullFace);
        // Vanilla supplies the full-frame dimensions for every target. The corrected
        // fragment derives its spacing from rasterization, so this uniform may be optimized out.
        GL.Uniform2(GL.GetUniformLocation(program.ProgramId, "frameSize"), (float)frameWidth, frameHeight);
        int axis = GL.GetUniformLocation(program.ProgramId, "isVertical");
        Assert.True(axis >= 0);
        int sampler = GL.GetUniformLocation(program.ProgramId, "inputTexture");
        Assert.True(sampler >= 0);
        GL.Uniform1(sampler, 0);
        horizontal.BindWithViewport();
        StateCache.Current.BindTexture(TextureTarget.Texture2D, 0, input.TextureId);
        GL.Uniform1(axis, 0); GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        vertical.BindWithViewport();
        StateCache.Current.BindTexture(TextureTarget.Texture2D, 0, horizontal[0].TextureId);
        GL.Uniform1(axis, 1); GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        float[] actual = vertical[0].ReadPixels();
        float[] weights = [.001422f,.004255f,.011001f,.024574f,.047431f,.0791f,.113978f,.141908f,.152663f,
            .141908f,.113978f,.0791f,.047431f,.024574f,.011001f,.004255f,.001422f];
        double total = 0;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int dx = x - width / 2, dy = y - height / 2;
            float expected = Math.Abs(dx) <= 8 && Math.Abs(dy) <= 8 ? 1000 * weights[dx + 8] * weights[dy + 8] : 0;
            float pixel = actual[(y * width + x) * 4];
            Assert.True(float.IsFinite(pixel));
            Assert.InRange(pixel, expected - .0001f, expected + .0001f);
            total += pixel;
        }
        double expectedEnergy = 1000 * Math.Pow(weights.Sum(), 2);
        Assert.InRange(total, expectedEnergy - .01, expectedEnergy + .01);
        Assert.True(actual[(height / 2 * width + width / 2) * 4] > 1);
        AssertNoGLError("destination-texel bloom blur");
    }
    #endregion
}
