using VanillaGraphicsExpanded.Rendering.RHI;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.PBR.Postprocessing.LocalExposure;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks source-local bloom extraction independently of destination-local final composition.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class BloomLocalExposureShaderTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Source correction precedes threshold selection for both camera modes and remains optional.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SourceLocalCorrectionPrecedesHighlightSelection(bool automatic, bool enabled)
    {
        EnsureShaderTestAvailable();
        var program = Programs.Create<BloomShaderProgram>(p => p.LocalExposureEnabled = true);
        using var source = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, [.04f, .04f, .04f, 1f]);
        using var spatial = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rg32f, [MathF.Log2(.04f), 1f]);
        using var history = ExposureTestStorage.Create(0);
        using var target = TestFramework.CreateTestGBuffer(1, 1, PixelInternalFormat.Rgba32f);
        program.SourceImage = source; program.SecondaryImage = source; program.LocalBase = spatial;
        program.ExposureBuffer = history;
        program.CaptureLocal(Settings(enabled), MathF.Log2(.18f));
        program.Capture(new(0, 0, 0, automatic ? 1 : 0), new(.06f, 0, 1, automatic ? -10 : 0));
        Draw(program, target);
        float expected = enabled ? MathF.Sqrt(.04f * .18f) : 0;
        Assert.InRange(target[0].ReadPixels()[0], expected - .00001f, expected + .00001f);
        Assert.Equal(new[] { .04f, .04f, .04f, 1f }, source.ReadPixels());
    }

    /// <summary>Every first-reduction source sample receives its own correction before their values are averaged.</summary>
    [Fact]
    public void FirstReductionCorrectsIndividualSources()
    {
        EnsureShaderTestAvailable();
        var program = Programs.Create<BloomShaderProgram>(p => p.LocalExposureEnabled = true);
        float[] values = [.04f, .16f, .64f, 2.56f];
        using var source = TestFramework.CreateTexture(2, 2, PixelInternalFormat.Rgba32f,
            values.SelectMany(v => new[] { v, v, v, 1f }).ToArray());
        using var spatial = TestFramework.CreateTexture(2, 2, PixelInternalFormat.Rg32f,
            values.SelectMany(v => new[] { MathF.Log2(v), 1f }).ToArray());
        using var target = TestFramework.CreateTestGBuffer(1, 1, PixelInternalFormat.Rgba32f);
        program.SourceImage = source; program.SecondaryImage = source; program.LocalBase = spatial;
        program.CaptureLocal(Settings(true), MathF.Log2(.18f));
        program.Capture(Vector4.Zero, new(0, 0, 1, 0));
        Draw(program, target);
        // Unequal sources distinguish averaging corrected samples from correcting the averaged source.
        float expected = values.Average(v => MathF.Sqrt(v * .18f));
        Assert.InRange(target[0].ReadPixels()[0], expected - .00002f, expected + .00002f);
        program.Capture(new(0, 0, 1, 0), Vector4.Zero);
        Draw(program, target);
        float unchanged = values.Average();
        Assert.InRange(target[0].ReadPixels()[0], unchanged - .00001f, unchanged + .00001f);
    }

    /// <summary>The real pyramid applies source gain once, then retains its normalized energy across variant transitions.</summary>
    [Fact]
    public void OwnedPyramidAppliesSourceGainOnceAndDisablesCleanly()
    {
        EnsureShaderTestAvailable();
        using var assets = new BinaryShaderApiFixture();
        using var platform = new EngineShaderPlatformScope();
        Assert.True(VgeShaderPrograms.RegisterAll(assets.Api));
        using var publication = new TestFrameLightingPublication();
        GpuShaderProgramLibraries.Get<BloomShaderProgram>(assets.Api, "pbr_bloom")!.FrameLightingInputs = publication.Buffer;
        GpuShaderProgramLibraries.Get<BloomShaderProgram>(assets.Api, "pbr_bloom_local_exposure")!.FrameLightingInputs = publication.Buffer;
        using var source = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, [.04f, .04f, .04f, 1f]);
        using var spatial = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rg32f, [MathF.Log2(.04f), 1f]);
        using var owner = new BloomRenderer();
        using var draw = new PostprocessDraw();
        var settings = new PostprocessParameters(1, 0, 0, 5, 0, 1, 16);
        foreach (bool enabled in new[] { true, false, true })
        {
            var pipeline = owner.Prepare(assets.Api, draw, 17, 9, 5, Settings(enabled), enabled ? spatial : null, MathF.Log2(.18f));
            {
    var commands=RHICommandContext.Current;
    using var scope12=commands.EnterBoundary(new("Tests.BloomLocal.Owner",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([pipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope12.Run(()=>owner.Render(commands, draw, source, (null, 0), settings));
}
            float expected = enabled ? MathF.Sqrt(.04f * .18f) : .04f;
            float[] actual = owner.Texture.ReadPixels();
            for (int pixel = 0; pixel < actual.Length; pixel += 4)
                Assert.InRange(actual[pixel], expected - .0003f, expected + .0003f);
        }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Private
    /// <summary>Defines bounded symmetric source correction with no diagnostic visualization.</summary>
    private static LocalExposureParameters Settings(bool enabled) => new(enabled, 2, 2, 1, .5f, .5f, 4, 4, false);

    /// <summary>Submits the actual bloom fragment program with deterministic raster state.</summary>
    private static void Draw(BloomShaderProgram program, GpuFramebuffer target)
    {
        using var draw = new PostprocessDraw();
        var pipeline = draw.Prepare(program,target.PostprocessPass());
        {
    var commands=RHICommandContext.Current;
    using var scope13=commands.EnterBoundary(new("Tests.BloomLocal.Extraction",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([pipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope13.Run(()=>draw.Submit(commands, pipeline,target.PostprocessPass(), program.CaptureBindings()));
}
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
}
