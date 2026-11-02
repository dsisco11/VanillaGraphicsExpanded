using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Executes production solar geometry and fragment coverage through owned shader modules.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class AtmosphereSunRasterTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{


    #region Disk rasterization
    /// <summary>The GPU segment model retains bounded geometry and CPU agreement at grazing incidence.</summary>
    [Fact]
    public void GrazingSegmentMathMatchesCpu()
    {
        EnsureContextValid();
        string vertex = "tests/complete-state.vsh";
        string fragment = "tests/sun-segment.fsh";
        using var program = GpuProgramObject.Adopt(BuiltShaderFixture.LoadFixtureProgram(vertex, fragment));
        using var vao = NativeResourceSetup.CreateVertexArray();
        using var framework = new ShaderTestFramework();
        using var target = framework.CreateTestGBuffer(1, 1, TextureFormat.Rgba32f);
        var layout = BuiltShaderFixture.Layout(program.ProgramId, "tests/sun-segment.fsh");
        OpenGLProgramSelection.Select(OpenGlStateCache.Current, program.ProgramId);
        OpenGLVertexSelection.Bind(OpenGlStateCache.Current, vao.VertexArrayId);
        GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.CullFace);
        using var inputs = new PackedUniformBuffer(16);
        byte[] inputBytes = new byte[16];
        for (int exponent = 2; exponent <= 7; exponent++)
        foreach (float sign in new[] { -1f, 1f })
        {
            float elevation = sign * (1 - MathF.Pow(10, -exponent)) * AtmosphereSolarDisk.AngularRadius;
            FramebufferTestSetup.BindForEngineRendering(target);
            UboPacking.WriteFloat(inputBytes, 0, elevation);
            inputs.SetBytes(inputBytes);
            Assert.True(VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindUniformSnapshot(GpuBindingRegistry.Ubo.ShaderInputs, inputs.Capture()));
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
        string vertex = "tests/sun-raster.vsh";
        string fragmentPath = displayTransfer ? "tests/sun-raster-display.fsh" : "tests/sun-raster-linear.fsh";
        string fragment = fragmentPath;
        using var program = GpuProgramObject.Adopt(BuiltShaderFixture.LoadFixtureProgram(vertex, fragment));
        using var vao = NativeResourceSetup.CreateVertexArray();
        using var color = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 64), Height = System.Math.Max(1, 64), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var depth = new DepthTexture(64, 64, TextureFormat.DepthComponent32f);
        using var glow = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 64), Height = System.Math.Max(1, 64), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var target = DynamicGpuFramebuffer.CreateMRT([color, glow], depth)!;
        var layout = BuiltShaderFixture.Layout(program.ProgramId, "tests/sun-raster.vsh", fragmentPath);
        OpenGLProgramSelection.Select(OpenGlStateCache.Current, program.ProgramId);
        OpenGLVertexSelection.Bind(OpenGlStateCache.Current, vao.VertexArrayId);
        GL.Enable(EnableCap.DepthTest); GL.DepthFunc(DepthFunction.Less); GL.DepthMask(true);
        GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.CullFace);
        using var inputs = new PackedUniformBuffer(64);
        byte[] inputBytes = new byte[64];
        UboPacking.WriteInt32(inputBytes, 4, displayTransfer ? 0 : 1);
        UboPacking.WriteVec4(inputBytes, 32, 3f, 2f, 1f, AtmosphereSolarDisk.AngularRadius);
        UboPacking.WriteFloat(inputBytes, 48, AtmosphereSolarDiskStorage.MaximumScale(new(3,2,1),1,1));
        Assertions.GpuImageSnapshot? baseline = null;
        double baselineCoverage = 0;
        try
        {
        for (int iteration = 0; iteration < 4; iteration++)
        {
            FramebufferTestSetup.BindForEngineRendering(target);
            GL.ClearColor(0, 0, 0, 0); GL.ClearDepth(iteration == 3 ? .5 : 1);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            float[] view = Vintagestory.API.MathTools.Mat4f.Create();
            view[12] = iteration * 37f; view[13] = iteration * -19f; view[14] = iteration * 123f;
            using var camera = TestFrameCamera.CreateSolar(64,64,view);
            Assert.True(VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindUniformSnapshot(GpuBindingRegistry.Ubo.Frame, camera.Capture()));
            UboPacking.WriteVec4(inputBytes, 16, 0f, 0f, -1f, iteration == 2 ? 0f : -1f);
            inputs.SetBytes(inputBytes);
            Assert.True(VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindUniformSnapshot(GpuBindingRegistry.Ubo.ShaderInputs, inputs.Capture()));
            GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
            using var glowPixels = new Assertions.GpuImageSnapshot(target[1]);
            ImageAssertions.Comparisons.CompareHistory(target[0], glowPixels, channelMask: 8).AssertEqual();
            if (!displayTransfer) ImageAssertions.Comparisons.CompareConstant(target[1], 0, channelMask: 1).AssertEqual();
            else ImageAssertions.Statistics.InspectCoverage(target[1], .88f, 1f).AssertFiniteRange();
            if (iteration == 0)
            {
                baseline = new(target[0]);
                baselineCoverage = ImageAssertions.Statistics.Statistics(target[0], channelMask: 8).Sum(3);
                float expected = displayTransfer ? 1.055f * MathF.Pow(.75f, 1 / 2.4f) - .055f : 3f;
                if (displayTransfer) expected -= 31.5f / (64f * 255f);
                Assert.InRange(Assertions.GpuLocalRead.Scalar(target[0], (32 * 64 + 32) * 4), expected - .00001f, expected + .00001f);
                Assert.Equal(0f, Assertions.GpuLocalRead.Scalar(target[0], 0));
            }
            else if (iteration == 1) ImageAssertions.Comparisons.CompareHistory(target[0], baseline!).AssertEqual();
            else if (iteration == 3) ImageAssertions.Comparisons.CompareConstant(target[0], 0).AssertEqual();
            else
            {
                double half = ImageAssertions.Statistics.Statistics(target[0], channelMask: 8).Sum(3);
                Assert.InRange(half / baselineCoverage, .47f, .53f);
            }
        }
        }
        finally { baseline?.Dispose(); }
    }
    #endregion
    #region Private
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.GPU.Assertions.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
        programs.Prepare("inspection", 0, 0);
        programs.Prepare("statistics", 0, 0);
    }
    #endregion
}
