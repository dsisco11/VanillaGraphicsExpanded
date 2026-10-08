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
        int vertex = shaders.Load(ShaderType.VertexShader, "tests/complete-state.vsh");
        int fragment = shaders.Load(ShaderType.FragmentShader, "tests/sun-segment.fsh");
        using var program = GpuProgramObject.Adopt(TerrainShaderTestFixture.Link(vertex, fragment));
        using var vao = GpuVao.Create();
        using var framework = new ShaderTestFramework();
        using var target = framework.CreateTestGBuffer(1, 1, PixelInternalFormat.Rgba32f);
        var layout = BuiltShaderFixture.Layout(program.ProgramId, "tests/sun-segment.fsh");
        StateCache.Current.UseProgram(program.ProgramId);
        StateCache.Current.BindVertexArray(vao.VertexArrayId);
        GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.CullFace);
        using var inputs = new PackedUniformBuffer(16);
        byte[] inputBytes = new byte[16];
        for (int exponent = 2; exponent <= 7; exponent++)
        foreach (float sign in new[] { -1f, 1f })
        {
            float elevation = sign * (1 - MathF.Pow(10, -exponent)) * AtmosphereSolarDisk.AngularRadius;
            target.BindWithViewport();
            UboPacking.WriteFloat(inputBytes, 0, elevation);
            inputs.SetBytes(inputBytes);
            Assert.True(inputs.TryBindToSlot(GpuBindingRegistry.Ubo.ShaderInputs));
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
        int vertex = shaders.Load(ShaderType.VertexShader, "tests/sun-raster.vsh");
        string fragmentPath = displayTransfer ? "tests/sun-raster-display.fsh" : "tests/sun-raster-linear.fsh";
        int fragment = shaders.Load(ShaderType.FragmentShader, fragmentPath);
        using var program = GpuProgramObject.Adopt(TerrainShaderTestFixture.Link(vertex, fragment));
        using var vao = GpuVao.Create();
        using var color = DynamicTexture2D.Create(64, 64, PixelInternalFormat.Rgba32f);
        using var depth = new DepthTexture(64, 64, PixelInternalFormat.DepthComponent32f);
        using var glow = DynamicTexture2D.Create(64, 64, PixelInternalFormat.Rgba32f);
        using var target = GpuFramebuffer.CreateMRT([color, glow], depth)!;
        var layout = BuiltShaderFixture.Layout(program.ProgramId, "tests/sun-raster.vsh", fragmentPath);
        StateCache.Current.UseProgram(program.ProgramId);
        StateCache.Current.BindVertexArray(vao.VertexArrayId);
        GL.Enable(EnableCap.DepthTest); GL.DepthFunc(DepthFunction.Less); GL.DepthMask(true);
        GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.CullFace);
        using var inputs = new PackedUniformBuffer(64);
        byte[] inputBytes = new byte[64];
        UboPacking.WriteInt32(inputBytes, 4, displayTransfer ? 0 : 1);
        UboPacking.WriteVec4(inputBytes, 32, 3f, 2f, 1f, AtmosphereSolarDisk.AngularRadius);
        float[]? baseline = null;
        for (int iteration = 0; iteration < 4; iteration++)
        {
            target.BindWithViewport();
            GL.ClearColor(0, 0, 0, 0); GL.ClearDepth(iteration == 3 ? .5 : 1);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            UboPacking.WriteVec3(inputBytes, 48, iteration * 37f, iteration * -19f, iteration * 123f);
            UboPacking.WriteVec4(inputBytes, 16, 0f, 0f, -1f, iteration == 2 ? 0f : -1f);
            inputs.SetBytes(inputBytes);
            Assert.True(inputs.TryBindToSlot(GpuBindingRegistry.Ubo.ShaderInputs));
            GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
            float[] pixels = target[0].ReadPixels();
            float[] glowPixels = target[1].ReadPixels();
            for (int pixel = 0; pixel < pixels.Length; pixel += 4)
            {
                Assert.Equal(pixels[pixel + 3], glowPixels[pixel + 3]);
                if (pixels[pixel + 3] == 0 || !displayTransfer) Assert.Equal(0f, glowPixels[pixel]);
                else Assert.InRange(glowPixels[pixel], .88f, 1f);
            }
            if (iteration == 0)
            {
                baseline = pixels;
                float expected = displayTransfer ? 1.055f * MathF.Pow(.75f, 1 / 2.4f) - .055f : 3f;
                if (displayTransfer) expected -= 31.5f / (64f * 255f);
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
