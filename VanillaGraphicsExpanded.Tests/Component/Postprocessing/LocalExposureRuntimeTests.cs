using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.PBR.Postprocessing.LocalExposure;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;

using Assertions = VanillaGraphicsExpanded.Tests.Support.GPU.Observations;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.Numerics;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;
namespace VanillaGraphicsExpanded.Tests.Component.Postprocessing;

/// <summary>Qualifies actual optional spatial ownership, full-area reduction and final grading integration.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
[Trait("Scenario", "HeadlessRuntime")]
public sealed class LocalExposureRuntimeTests(HeadlessGLFixture fixture, ITestOutputHelper output) : LumOnShaderFunctionalTestBase(fixture)
{


    private FinalDisplayShaderProgram? finalProgram;
    #region Public API
    #region Spatial reduction
    /// <summary>Odd source partitions cover every pixel and reproduce independent physical log means across storage scales.</summary>
    [Theory]
    [InlineData(.000244140625f)]
    [InlineData(1f)]
    [InlineData(64f)]
    public void CoarseOddSizedReductionMatchesPhysicalReference(float scale)
    {
        // Arrange: Build an odd-sized luminance field and matching frozen frame scale for an independent box reference.
        EnsureShaderTestAvailable();
        Vector3[] colors = Enumerable.Range(0, 13 * 7).Select(i => new Vector3(MathF.Pow(2, -18 + (i % 17) * .8f))).ToArray();
        using var scene = Scene(colors, 13, 7, scale);
        using var publication = new TestFrameLightingPublication(); publication.Capture(scale);
        using var owner = new LocalExposureRenderer(); owner.FrameLightingInputs = publication.Buffer;
        // Act and assert: Render the full odd footprint and compare logarithms and filtered values against the independent reduction.
        var result = owner.Render(Programs.Api, scene, Candidate());
        Assert.Equal(TextureFormat.Rg32f, result.InternalFormat);
        Assert.Equal(2, result.Width); Assert.Equal(2, result.Height);
        double[] expected = Reduce(colors, 13, 7, 2, 2);
        var logarithms = expected;
        var filtered = Enumerable.Range(0,4).Select(i => Filter(expected,2,2,i)).ToArray();
        ImageAssertions.Comparisons.CompareComponentDoubles(owner.Logarithms, logarithms, .000008, 1).AssertEqual();
        ImageAssertions.Comparisons.CompareConstant(owner.Logarithms, 1, channelMask: 2).AssertEqual();
        ImageAssertions.Comparisons.CompareComponentDoubles(result, filtered, .000008, 1).AssertEqual();
        ImageAssertions.Comparisons.CompareConstant(result, 1, channelMask: 2).AssertEqual();
        Assert.Equal(1, owner.Generation);
    }

    /// <summary>One valid last pixel is retained without a sparse sampling shortcut, while empty cells remain explicitly missing.</summary>
    [Fact]
    public void SparseEmitterAndInvalidCellsHaveExplicitValidity()
    {
        // Arrange: Mix invalid values, empty cells, and one sparse valid emitter in an odd-sized image.
        EnsureShaderTestAvailable();
        Vector3[] pixels = new Vector3[13 * 7];
        pixels[0] = new(float.NaN); pixels[1] = new(float.PositiveInfinity); pixels[2] = new(-1, 1, 1);
        pixels[^1] = new(.015625f);
        using var scene = Scene(pixels, 13, 7);
        using var publication = new TestFrameLightingPublication();
        using var owner = new LocalExposureRenderer(); owner.FrameLightingInputs = publication.Buffer;
        // Act and assert: Reduce the sparse scene and require invalid/empty cells to remain invalid while the last emitter is retained.
        owner.Render(Programs.Api, scene, Candidate());
        foreach (var actual in new[] { owner.Logarithms, owner.BaseTexture })
        {
            ImageAssertions.Comparisons.CompareConstant(actual, 0, new(new(0),0,0,0,2,1)).AssertEqual();
            ImageAssertions.Comparisons.CompareConstant(actual, 0, new(new(0),0,1,0,1,1)).AssertEqual();
            ImageAssertions.Statistics.Inspect(actual, -6.000001f, -5.999999f, new(new(0),1,1,0,1,1), channelMask: 1).AssertFiniteRange();
            ImageAssertions.Comparisons.CompareConstant(actual, 1, new(new(0),1,1,0,1,1), channelMask: 2).AssertEqual();
        }
    }
    #endregion

    #region Resource publication
    /// <summary>Stable frames reuse images; current-frame overwrite, size change and retirement never expose stale data.</summary>
    [Fact]
    public void OwnershipReusesStableStorageAndWithdrawsRetiredPublication()
    {
        // Arrange: Prepare valid and black scenes with one local-exposure owner and retained frame publication.
        EnsureShaderTestAvailable();
        using var publication = new TestFrameLightingPublication();
        using var scene = Scene(Enumerable.Repeat(new Vector3(.00390625f), 91).ToArray(), 13, 7);
        using var black = Scene(new Vector3[91], 13, 7);
        using var owner = new LocalExposureRenderer(); owner.FrameLightingInputs = publication.Buffer;
        // Act and assert: Reuse same-size storage, clear it for black input, retire on resize/dispose, then permit fresh publication.
        Assert.Throws<InvalidOperationException>(() => owner.BaseTexture);
        var first = owner.Render(Programs.Api, scene, Candidate());
        var logs = owner.Logarithms;
        for (int i = 0; i < 3; i++) Assert.Same(first, owner.Render(Programs.Api, scene, Candidate()));
        Assert.Equal(1, owner.Generation);
        Assert.Same(first, owner.Render(Programs.Api, black, Candidate()));
        ImageAssertions.Comparisons.CompareConstant(first, 0).AssertEqual();
        var resized = owner.Render(Programs.Api, scene, Candidate() with { Width = 3, Height = 2 });
        Assert.NotSame(first, resized); Assert.False(first.IsValid); Assert.False(logs.IsValid);
        Assert.Equal(2, owner.Generation);
        owner.Dispose();
        Assert.False(resized.IsValid);
        Assert.Throws<InvalidOperationException>(() => owner.BaseTexture);
        Assert.Throws<InvalidOperationException>(() => owner.Logarithms);
        var restored = owner.Render(Programs.Api, scene, Candidate());
        Assert.True(restored.IsValid); Assert.Equal(3, owner.Generation);
    }

    /// <summary>Failed executable preparation after retirement exposes no stale image and recovers only through a new reload attempt.</summary>
    [Fact]
    public void FailedPreparationWithdrawsPublicationAndReloadRecovers()
    {
        // Arrange: Establish a complete owner and inject a controlled shader-input failure during reload.
        EnsureShaderTestAvailable();
        using var assets = new BinaryShaderApiFixture();
        using var platform = new EngineShaderPlatformScope();
        var lifetime = ClientRenderingLifetime.Create(assets.Api, VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderProgramLibraries.Require(assets.Api));
        lifetime.Attach(() => { });
        using var publication = new TestFrameLightingPublication();
        using var scene = Scene([new(.01f)], 1, 1);
        using var owner = new LocalExposureRenderer(); owner.FrameLightingInputs = publication.Buffer;
        try
        {
            // Act and assert: Render once, force executable preparation to fail, then recover only after a new reload attempt.
            var first = owner.Render(assets.Api, scene, Candidate());
            ClientRenderingLifetime.BeginNativeReload(); owner.Dispose(); ClientRenderingLifetime.CompleteNativeReload();
            Assert.False(first.IsValid);
            assets.BeforeRead = _ => throw new IOException("controlled local exposure preparation failure");
            Assert.Throws<InvalidOperationException>(() => owner.Render(assets.Api, scene, Candidate()));
            Assert.Throws<InvalidOperationException>(() => owner.BaseTexture);
            Assert.Throws<InvalidOperationException>(() => owner.Logarithms);
            assets.BeforeRead = null;
            ClientRenderingLifetime.BeginNativeReload(); owner.Dispose(); ClientRenderingLifetime.CompleteNativeReload();
            Assert.True(owner.Render(assets.Api, scene, Candidate()).IsValid);
        }
        finally { owner.Dispose(); lifetime.Shutdown(); }
    }
    #endregion

    #region Final presentation
    /// <summary>Manual and automatic final exposure correct scene locally while adding already prepared bloom outside destination-local gain.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FinalCompositionUsesAppliedExposureAndIgnoresMeterMetadata(bool automatic)
    {
        // Arrange: Pair scene colors with matching local-base values and select automatic or manual exposure.
        EnsureShaderTestAvailable();
        Vector3[] colors = [new(.00004f, .00003f, .00001f), new(.01736f, .01302f, .00434f)];
        float[] bases = colors.SelectMany(c => new[] { (float)Math.Log2(Luminance(c)), 1f }).ToArray();
        float metered = (bases[0] + bases[2]) * .5f;
        using var source = Scene(colors, 2, 1);
        using var glare = Scene([new(.00001f)], 1, 1);
        using var empty = Scene([Vector3.Zero], 1, 1);
        using var baseImage = TestFramework.CreateTexture(2, 1, TextureFormat.Rg32f, bases);
        using var history = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = "Tests.LocalFinal.Exposure" }).RequireResource();
        history.UploadData(new[] { 6f, 0f, metered, 1f, 20f, 1f, 0f, 0f });
        using var grading = new ColorGradingLut();
        Vector4 controls = new(1, 1, 1, 0);
        var lookup = grading.Prepare(Programs.Api, new(controls.X, controls.Y, controls.Z, controls.W));
        using var global = Final(source, glare, empty, baseImage, history, lookup, Candidate() with { Enabled = false }, automatic);
        using var local = Final(source, glare, empty, baseImage, history, lookup, Candidate(), automatic);
        // Act and assert: Compare local/global composition to independent exposure math and ensure invalid meter metadata is irrelevant.
        for (int i = 0; i < 2; i++)
        {
            double stops = Math.Clamp((Math.Log2(.18)-6-bases[i * 2]) * .5, -4, 4);
            Vector3 exposed = (colors[i] * (float)Math.Pow(2, stops) + new Vector3(.00001f)) * 64f;
            Vector3 expected = FilmicGradingReference.Lookup(exposed, controls);
            for (int c = 0; c < 3; c++) Assert.InRange(Math.Abs(GpuLocalRead.Scalar(local.Texture,i*4+c) - FilmicGradingReference.Quantize(expected[c], i)), 0, 1.001f / 255);
        }
        Assert.True(GpuLocalRead.Scalar(local.Texture,0)>GpuLocalRead.Scalar(global.Texture,0)); Assert.True(GpuLocalRead.Scalar(local.Texture,4)<GpuLocalRead.Scalar(global.Texture,4));
        foreach (float validity in new[] { 0f, float.NaN, float.PositiveInfinity })
        {
            history.UploadData(new[] { 6f, 0f, metered, validity, 20f, 1f, 0f, 0f });
            using var missing = Final(source, glare, empty, baseImage, history, lookup, Candidate(), automatic);
            ImageAssertions.Comparisons.CompareHistory(missing.Texture,local).AssertEqual();
        }
        history.UploadData(new[] { 6f, 0f, metered, 1f, 20f, 1f, 0f, 0f });
        using var disabledVariant = Final(source, glare, empty, baseImage, history, lookup, Candidate() with { Enabled = false }, automatic, optionalVariant: false);
        ImageAssertions.Comparisons.CompareHistory(disabledVariant.Texture,global).AssertEqual();
        Assert.Equal(1, grading.Generation);
    }

    /// <summary>Bloom crosses different receiver corrections unchanged while scene and shafts retain their local multiplier.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FinalBloomIgnoresReceiverGainWhileShaftsRetainIt(bool automatic)
    {
        // Arrange: Supply distinct scene, bloom, and shaft radiances with differing receiver-local gains.
        EnsureShaderTestAvailable();
        using var scene = Scene([new(.001f), new(.001f)], 2, 1);
        using var bloom = Scene([new(.002f)], 1, 1);
        using var shafts = Scene([new(.0005f)], 1, 1);
        using var spatial = TestFramework.CreateTexture(2, 1, TextureFormat.Rg32f, [-10f, 1f, -9f, 1f]);
        using var history = ExposureTestStorage.Create(6);
        using var grading = new ColorGradingLut();
        Vector4 controls = new(1, 1, 1, 0);
        var lookup = grading.Prepare(Programs.Api, new(controls.X, controls.Y, controls.Z, controls.W));
        // Act and assert: Compose each receiver and verify local correction affects scene/shafts while source-corrected bloom is unchanged.
        using var actual = Final(scene, bloom, shafts, spatial, history, lookup, Candidate(), automatic);
        for (int pixel = 0; pixel < 2; pixel++)
        {
            // The independently computed local gain affects scene and shafts only. Bloom is already source-corrected.
            double stops = Math.Clamp((Math.Log2(.18) - 6 - (-10 + pixel)) * .5, -4, 4);
            float exposed = (.0015f * (float)Math.Pow(2, stops) + .002f) * 64;
            Vector3 expected = FilmicGradingReference.Lookup(new(exposed), controls);
            for (int channel = 0; channel < 3; channel++)
                Assert.InRange(Math.Abs(GpuLocalRead.Scalar(actual.Texture,pixel*4+channel) - FilmicGradingReference.Quantize(expected[channel], pixel)), 0, 1.001f / 255);
        }
    }

    /// <summary>Captured physical inputs receive bounded display-relative correction through the unchanged filmic lookup.</summary>
    [Fact]
    public void CapturedInputReceivesDisplayRelativeShadowLift()
    {
        // Arrange: Provide dark and bright scene samples with authored metering and local-base values.
        EnsureShaderTestAvailable();
        Vector3[] colors = [new(.00006592794125f, .0000526507864f, .00001556631946f), new(.03469f, .02133f, .003867f)];
        float[] bases = colors.SelectMany(c => new[] { (float)Math.Log2(Luminance(c)), 1f }).ToArray();
        const float metered = -12.7348446f;
        using var source = Scene(colors, 2, 1);
        using var zero = Scene([Vector3.Zero], 1, 1);
        using var baseImage = TestFramework.CreateTexture(2, 1, TextureFormat.Rg32f, bases);
        using var history = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = "Tests.CapturedMean.Exposure" }).RequireResource();
        history.UploadData(new[] { 6f, 0f, metered, 1f, 10.26f, 1f, 0f, 0f });
        using var grading = new ColorGradingLut();
        Vector4 controls = new(1, 1, 1.111798286f, .143282309f);
        var lookup = grading.Prepare(Programs.Api, new(controls.X, controls.Y, controls.Z, controls.W));
        using var global = Final(source, zero, zero, baseImage, history, lookup, Candidate() with { Enabled = false }, true, controls);
        using var local = Final(source, zero, zero, baseImage, history, lookup, Candidate(), true, controls);
        // Act and assert: Compare captured-input output against the unchanged grading LUT and independently computed shadow lift.
        for (int i = 0; i < 2; i++)
        {
            double stops = Math.Clamp((Math.Log2(.18)-6-bases[i * 2]) * .5, -4, 4);
            Vector3 expected = FilmicGradingReference.Lookup(colors[i] * (float)Math.Pow(2, 6 + stops), controls);
            for (int c = 0; c < 3; c++) Assert.InRange(Math.Abs(GpuLocalRead.Scalar(local.Texture,i*4+c) - FilmicGradingReference.Quantize(expected[c], i)), 0, 1.001f / 255);
            if (i == 0) Assert.InRange(stops, 2, 4);
        }
    }

    /// <summary>Missing spatial validity and exact black retain global composition even when generated glare is nonzero.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NeutralGuidancePreservesSceneAndGlare(bool black)
    {
        // Arrange: Select black or low-light scene input with neutral guidance and a separate glare sample.
        EnsureShaderTestAvailable();
        using var scene = Scene(black ? [Vector3.Zero, Vector3.Zero] : [new(.00004f), new(.001f)], 2, 1);
        using var glare = Scene([new(.001f)], 1, 1);
        using var empty = Scene([Vector3.Zero], 1, 1);
        using var spatial = TestFramework.CreateTexture(1, 1, TextureFormat.Rg32f, black ? [-14, 1] : [0, 0]);
        using var history = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = "Tests.LocalFinal.NeutralGuidance" }).RequireResource();
        history.UploadData(new[] { 6f, 0f, -8f, 1f, 6f, 0f, 0f, 0f });
        using var grading = new ColorGradingLut(); var lookup = grading.Prepare(Programs.Api, new(1, 1, 1, 0));
        using var baseline = Final(scene, glare, empty, spatial, history, lookup, Candidate() with { Enabled = false }, true);
        using var local = Final(scene, glare, empty, spatial, history, lookup, Candidate(), true);
        // Act and assert: Compare neutral guidance with global output and ensure nonblack glare still reaches presentation.
        ImageAssertions.Comparisons.CompareHistory(local.Texture,baseline).AssertEqual();
        Assert.True(ImageAssertions.Statistics.Inspect(local.Texture,channelMask:7).Maximum>0);
    }
    #endregion

    #endregion

    #region Protected API
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
        programs.Prepare("inspection", 0, 0);
    }
    #endregion

    #region Private
    #region GPU fixtures
    /// <summary>Creates explicit stored scene RGB without modifying the input calibration.</summary>
    private DynamicTexture2D Scene(Vector3[] pixels, int width, int height, float scale = 1) => TestFramework.CreateTexture(width, height,
        TextureFormat.Rgba32f, pixels.SelectMany(c => new[] { c.X * scale, c.Y * scale, c.Z * scale, 1f }).ToArray());

    /// <summary>Runs the actual final program with local guidance and published global exposure storage.</summary>
    private GpuImageSnapshot Final(DynamicTexture2D source, DynamicTexture2D glare, DynamicTexture2D empty, DynamicTexture2D localBase,
        GpuShaderStorageBuffer history, DynamicTexture3D lookup, LocalExposureParameters settings, bool automatic, Vector4? controls = null,
        bool optionalVariant = true)
    {
        var program = optionalVariant
            ? finalProgram ??= Programs.Create<FinalDisplayShaderProgram>(p => p.LocalExposureEnabled = true)
            : Programs.Create<FinalDisplayShaderProgram>();
        var bindings = program.ProgramLayout.BinaryInterface!.PreparedBindings;
        Assert.False(bindings.Resolve(GpuBindingEntry.Identity(ShaderBindingKind.StorageBlock, "CameraAdaptation")).Active);
        Assert.Equal(optionalVariant, bindings.Resolve(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "localBaseImage")).Active);
        using var target = TestFramework.CreateTestGBuffer(2, 1, TextureFormat.Rgba32f);
        using var camera = TestFrameCamera.CreateIdentity(2, 1);
        using var draw = new PostprocessDraw();
        program.FrameInputs = camera; program.SceneImage = source; program.BloomImage = glare; program.ShaftImage = empty;
        program.GradingLookup = lookup; program.ExposureBuffer = history;
        program.LocalBase = localBase;
        var grade = controls ?? new(1, 1, 1, 0);
        program.Capture(false, new(new(grade.X, grade.Y, grade.Z, grade.W), 0, 0, 0, 0, 0, 0), 6, automatic, MathF.Log2(.18f));
        program.CaptureLocal(settings);
        var pipeline = draw.Prepare(program,target.PostprocessPass());
        {
    var c=RHICommandContext.Current;
    using var scope62=c.EnterBoundary(new("Tests.LocalExposure.Final",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([pipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope62.Run(()=>draw.Submit(c, pipeline,target.PostprocessPass(), program.CaptureBindings()));
}
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        return new(target[0]);
    }
    #endregion

    #region Independent references
    /// <summary>Returns an explicit small candidate rather than a selected runtime policy.</summary>
    private static LocalExposureParameters Candidate() => new(true, 2, 2, 1, .5f, .5f, 4, 4, false);

    /// <summary>Computes luminance in double precision independently of the shader's floating-point dot product.</summary>
    private static double Luminance(Vector3 c) => .2126 * c.X + .7152 * c.Y + .0722 * c.Z;

    /// <summary>Partitions every odd-sized source texel into exactly one coarse cell and averages valid log luminance.</summary>
    private static double[] Reduce(Vector3[] source, int width, int height, int columns, int rows)
    {
        double[] result = new double[columns * rows];
        for (int y = 0; y < rows; y++) for (int x = 0; x < columns; x++)
        {
            double sum = 0; int count = 0;
            for (int sy = y * height / rows; sy < (y + 1) * height / rows; sy++)
                for (int sx = x * width / columns; sx < (x + 1) * width / columns; sx++)
                {
                    sum += Math.Log2(Luminance(source[sy * width + sx])); count++;
                }
            result[y * columns + x] = sum / count;
        }
        return result;
    }

    /// <summary>Evaluates independently normalized binomial spatial and Gaussian range weights.</summary>
    private static double Filter(double[] input, int width, int height, int pixel)
    {
        int cx = pixel % width, cy = pixel / width;
        double numerator = 0, denominator = 0;
        for (int y = Math.Max(0, cy - 1); y <= Math.Min(height - 1, cy + 1); y++)
            for (int x = Math.Max(0, cx - 1); x <= Math.Min(width - 1, cx + 1); x++)
            {
                double difference = input[y * width + x] - input[pixel];
                double weight = (x == cx ? 2 : 1) * (y == cy ? 2 : 1) * Math.Exp(-difference * difference / 2);
                numerator += input[y * width + x] * weight; denominator += weight;
            }
        return numerator / denominator;
    }
    #endregion

    #endregion
}

