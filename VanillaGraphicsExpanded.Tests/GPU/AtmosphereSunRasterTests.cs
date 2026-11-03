using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Executes production solar geometry and fragment coverage through owned shader modules.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class AtmosphereSunRasterTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>The GPU segment model retains bounded geometry and CPU agreement at grazing incidence.</summary>
    [Fact]
    public void GrazingSegmentMathMatchesCpu()
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var program = programs.Create<SolarSegmentProgram>();
        using var target = CreateRenderTarget(1, 1, TextureFormat.Rgba32f);
        var pass = FramebufferTestSetup.DescribeAttachments(target,
            [new(AttachmentIndex: 0, Load: AttachmentLoad.Clear, Clear: ColorClearValue.Float(0, 0, 0, 0))]);
        var commands = RHICommandContext.Current;
        var layout = new RHIVertexLayout([]);
        using var geometry = new ArrayGraphicsGeometry(layout, PrimitiveTopology.Triangles,
            new Dictionary<int, GpuVbo>(), proceduralVertices: 3);
        using var pipeline = commands.CreateGraphicsPipeline(new(program.Shader, layout,
            commands.DescribeRenderTargets(pass), DynamicPipelineState.Viewport)).RequireResource();

        for (int exponent = 2; exponent <= 7; exponent++)
        foreach (float sign in new[] { -1f, 1f })
        {
            // Arrange: place the segment just inside or outside the solar disk at each grazing scale.
            float elevation = sign * (1 - MathF.Pow(10, -exponent)) * AtmosphereSolarDisk.AngularRadius;
            program.Capture(elevation);

            // Act: submit exactly three procedural vertices through the production pass and pipeline.
            using var boundary = commands.EnterBoundary(new("Tests.SolarSegment", true,
                graphics: [pipeline], operations: BoundaryOperations.RenderPasses));
            boundary.Run(() =>
            {
                commands.BeginRenderPass(pass);
                commands.SetPipeline(pipeline);
                commands.SetDynamicState(new() { Viewport = commands.PassViewport });
                commands.BindResources(program.CaptureBindings());
                commands.Draw(geometry.Inputs, new(0, 3));
                commands.EndRenderPass();
            });

            // Assert: retain the independent analytic visibility and visible-centroid oracle.
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
        using var programs = new ComponentShaderPrograms();
        var program = programs.Create<SunProgram>();
        using var frameLighting = new TestFrameLightingPublication();
        var frameLightingBuffer = frameLighting.Buffer;
        program.FrameLightingInputs = frameLightingBuffer;
        using var color = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription
        { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = 64, Height = 64, MipLevels = 1,
            Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = 0,
            Label = null }).RequireResource();
        using var depth = new DepthTexture(64, 64, TextureFormat.DepthComponent32f);
        using var glow = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription
        { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = 64, Height = 64, MipLevels = 1,
            Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = 0,
            Label = null }).RequireResource();
        using var target = DynamicGpuFramebuffer.CreateMRT([color, glow], depth)!;
        var commands = RHICommandContext.Current;
        var layout = new RHIVertexLayout([]);
        using var geometry = new ArrayGraphicsGeometry(layout, PrimitiveTopology.Triangles,
            new Dictionary<int, GpuVbo>(), proceduralVertices: 3);
        var pass = FramebufferTestSetup.DescribeAttachments(target,
            [new(AttachmentIndex: 0), new(AttachmentIndex: 1)]);
        var metadata = commands.DescribeRenderTargets(pass);
        var additive = new ColorBlendDesc { Enabled = true, SourceRgb = BlendFactor.One,
            DestinationRgb = BlendFactor.One, SourceAlpha = BlendFactor.Zero, DestinationAlpha = BlendFactor.One };
        using var pipeline = commands.CreateGraphicsPipeline(new(program.Shader, layout, metadata,
            DynamicPipelineState.Viewport, depthStencil: new() { DepthTest = true, DepthWrite = true,
                // This fixture's far-plane output equals a clear depth of one; production SunDrawPass disables depth.
                DepthComparison = ComparisonFunction.LessOrEqual },
            blending: [additive, additive])).RequireResource();

        using var displayProgram = displayTransfer ? programs.Create<FinalDisplayShaderProgram>() : null;
        if (displayProgram is not null) displayProgram.FrameLightingInputs = frameLightingBuffer;
        using var grading = displayTransfer ? new ColorGradingLut() : null;
        var gradingLookup = displayTransfer ? grading!.Prepare(programs.Api, new(1, 1, 1, 0)) : null;
        using var zero = displayTransfer ? commands.CreateDynamicTexture2D(new RHITextureDescription
        { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = 1, Height = 1, MipLevels = 1,
            Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = 0,
            Label = null }).RequireResource() : null;
        zero?.UploadDataImmediate(new float[4]);
        using var exposure = displayTransfer ? ExposureTestStorage.Create(0) : null;
        using var displayTarget = displayTransfer ? CreateRenderTarget(64, 64, TextureFormat.Rgba32f) : null;

        Assertions.GpuImageSnapshot? baseline = null;
        Assertions.GpuImageSnapshot? baselineGlow = null;
        Assertions.GpuImageSnapshot? baselineDisplay = null;
        double baselineCoverage = 0;
        double baselineRadiance = 0;
        double baselineDisplayRadiance = 0;
        try
        {
            for (int iteration = 0; iteration < 4; iteration++)
            {
                // Arrange: preserve the original camera translations and the half-depth occluder case.
                float[] view = Vintagestory.API.MathTools.Mat4f.Create();
                view[12] = iteration * 37f; view[13] = iteration * -19f; view[14] = iteration * 123f;
                using var camera = TestFrameCamera.CreateSolar(64, 64, view);
                program.Frame = camera;
                Vector3 radiance = new(3, 2, 1);
                AtmosphereLighting lighting = SolarRasterLighting.Create(radiance, iteration == 2 ? 0f : -1f);
                float maximumScale = AtmosphereSolarDiskStorage.MaximumScale(radiance, 1, 1);
                program.DrawInputs.Capture(lighting, maximumScale);

                var currentPass = FramebufferTestSetup.DescribeAttachments(target,
                    [new(AttachmentIndex: 0, Load: AttachmentLoad.Clear, Clear: ColorClearValue.Float(0, 0, 0, 0)),
                     new(AttachmentIndex: 1, Load: AttachmentLoad.Clear, Clear: ColorClearValue.Float(0, 0, 0, 0))],
                    new(DepthLoad: AttachmentLoad.Clear, ClearDepth: iteration == 3 ? .5f : 1f));
                // Act: submit the shipped production solar executable and its captured atmosphere inputs.
                using var boundary = commands.EnterBoundary(new("Tests.SolarRaster", true,
                    graphics: [pipeline], operations: BoundaryOperations.RenderPasses));
                boundary.Run(() =>
                {
                    commands.BeginRenderPass(currentPass);
                    commands.SetPipeline(pipeline);
                    commands.SetDynamicState(new() { Viewport = commands.PassViewport });
                    commands.BindResources(program.CaptureBindings());
                    commands.Draw(geometry.Inputs, new(0, 3));
                    commands.EndRenderPass();
                });

                if (displayTransfer)
                {
                    displayProgram!.FrameInputs = camera;
                    displayProgram.GradingLookup = gradingLookup!;
                    displayProgram.SceneImage = color;
                    displayProgram.BloomImage = zero!;
                    displayProgram.ShaftImage = zero!;
                    displayProgram.ExposureBuffer = exposure;
                    displayProgram.Capture(false, new(new(1, 1, 1, 0), Vector4.Zero, Vector4.Zero), Vector4.Zero);
                    using var draw = new PostprocessDraw();
                    var displayPass = displayTarget!.PostprocessPass();
                    var displayPipeline = draw.Prepare(displayProgram, displayPass);
                    using var displayBoundary = commands.EnterBoundary(new("Tests.SolarDisplay", true,
                        graphics: [displayPipeline], operations: BoundaryOperations.RenderPasses));
                    displayBoundary.Run(() => draw.Submit(commands, displayPipeline, displayPass,
                        displayProgram.CaptureBindings()));
                }

                // The HDR pipeline deliberately preserves attachment alpha. Its green channel is the
                // solar visibility marker; compare its support and integrated radiance independently.
                ImageAssertions.Comparisons.CompareConstant(target[1], 0, channelMask: 1).AssertEqual();
                double coverage = ImageAssertions.Statistics.Statistics(target[1], channelMask: 2).Sum(1);
                double radianceSum = ImageAssertions.Statistics.Statistics(target[0], channelMask: 1).Sum(0);
                if (iteration == 0)
                {
                    baseline = new(target[0]);
                    baselineGlow = new(target[1]);
                    baselineCoverage = coverage;
                    baselineRadiance = radianceSum;
                    Assert.True(baselineCoverage > 0, "The unobstructed solar disk must publish visible coverage.");
                    Assert.True(baselineRadiance > 0, "The unobstructed solar disk must publish positive HDR radiance.");
                    if (displayTransfer)
                    {
                        baselineDisplay = new(displayTarget![0]);
                        baselineDisplayRadiance = ImageAssertions.Statistics.Statistics(displayTarget[0], channelMask: 1).Sum(0);
                        Vector3 cpuLookup = FilmicGradingReference.Lookup(radiance, new(1, 1, 1, 0));
                        Vector3 expectedDisplay = cpuLookup;
                        expectedDisplay = new(
                            FilmicGradingReference.Quantize(expectedDisplay.X, 32, 32),
                            FilmicGradingReference.Quantize(expectedDisplay.Y, 32, 32),
                            FilmicGradingReference.Quantize(expectedDisplay.Z, 32, 32));
                        float[] actual = displayTarget![0].ReadPixelsRegion(32, 32, 1, 1);
                        for (int channel = 0; channel < 3; channel++)
                            Assert.InRange(Math.Abs(actual[channel] - expectedDisplay[channel]), 0f, 1.001f / 255);
                        Assert.True(baselineDisplayRadiance > 0, "The production display pass must retain visible solar color.");
                    }
                    else Assert.InRange(Assertions.GpuLocalRead.Scalar(target[0], (32 * 64 + 32) * 4), 3f - .00001f, 3f + .00001f);
                    Assert.Equal(0f, Assertions.GpuLocalRead.Scalar(target[0], 0));
                    Assert.True(coverage > 0, "Solar coverage must be represented independently of preserved attachment alpha.");
                }
                else if (iteration == 1)
                {
                    ImageAssertions.Comparisons.CompareHistory(target[0], baseline!).AssertEqual();
                    ImageAssertions.Comparisons.CompareHistory(target[1], baselineGlow!, channelMask: 2).AssertEqual();
                    if (displayTransfer) ImageAssertions.Comparisons.CompareHistory(displayTarget![0], baselineDisplay!).AssertEqual();
                }
                else if (iteration == 3)
                {
                    ImageAssertions.Comparisons.CompareConstant(target[0], 0).AssertEqual();
                    ImageAssertions.Comparisons.CompareConstant(target[1], 0, channelMask: 2).AssertEqual();
                    if (displayTransfer)
                    {
                        // The display contract composites black RGB into an opaque output alpha.
                        ImageAssertions.Comparisons.CompareConstant(displayTarget![0], 0, channelMask: 7).AssertEqual();
                        ImageAssertions.Comparisons.CompareConstant(displayTarget[0], 1, channelMask: 8).AssertEqual();
                    }
                }
                else
                {
                    Assert.InRange(radianceSum / baselineRadiance, .47, .53);
                    Assert.InRange(coverage / baselineCoverage, .47, .53);
                    if (displayTransfer)
                    {
                        double displayRadiance = ImageAssertions.Statistics.Statistics(displayTarget![0], channelMask: 1).Sum(0);
                        Assert.InRange(displayRadiance / baselineDisplayRadiance, .47, .53);
                    }
                }

                if (displayTransfer && iteration == 2)
                {
                    Vector3 expectedDisplay = FilmicGradingReference.Lookup(radiance, new(1, 1, 1, 0));
                    expectedDisplay = new(
                        FilmicGradingReference.Quantize(expectedDisplay.X, 32, 32),
                        FilmicGradingReference.Quantize(expectedDisplay.Y, 32, 32),
                        FilmicGradingReference.Quantize(expectedDisplay.Z, 32, 32));
                    float[] actual = displayTarget![0].ReadPixelsRegion(32, 32, 1, 1);
                    for (int channel = 0; channel < 3; channel++)
                        Assert.InRange(Math.Abs(actual[channel] - expectedDisplay[channel]), 0f, 1.001f / 255);
                }
            }
        }
        finally { baseline?.Dispose(); baselineGlow?.Dispose(); baselineDisplay?.Dispose(); }
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
