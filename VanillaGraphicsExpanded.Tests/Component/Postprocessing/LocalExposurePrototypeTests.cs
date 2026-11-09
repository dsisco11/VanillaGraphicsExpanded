using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.PBR.Postprocessing.LocalExposure;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;
using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;

using Assertions = VanillaGraphicsExpanded.Tests.Support.GPU.Observations;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.Numerics;
namespace VanillaGraphicsExpanded.Tests.Component.Postprocessing;

/// <summary>Qualifies a small current-frame local exposure candidate without installing runtime policy.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class LocalExposurePrototypeTests(HeadlessGLFixture fixture, ITestOutputHelper output)
    : LumOnShaderFunctionalTestBase(fixture)
{


    #region Public API
    #region Physical inputs
    /// <summary>Uniform scenes and identity compression remain independent of arbitrary brightness floors.</summary>
    [Theory]
    [InlineData(-24f)]
    [InlineData(-12f)]
    [InlineData(8f)]
    public void UniformFramesRemainIdentity(float logarithm)
    {
        EnsureShaderTestAvailable();
        // Arrange
        Vector3[] colors = Enumerable.Repeat(new Vector3(MathF.Pow(2, logarithm)), 16 * 8).ToArray();
        // Act
        using var result = Pipeline(colors, 16, 8, 1, -logarithm, new(.5f), new(4));
        // Assert
        ImageAssertions.Comparisons.CompareConstant(result[1].Texture,logarithm,tolerance:.00001f,channelMask:1).AssertEqual();
        ImageAssertions.Comparisons.CompareConstant(result[1].Texture,1,channelMask:2).AssertEqual();
        ImageAssertions.Comparisons.CompareConstant(result[2].Texture,colors[0].X,tolerance:Math.Max(1e-10,colors[0].X*.00002),channelMask:1).AssertEqual();
        ImageAssertions.Comparisons.CompareConstant(result[2].Texture,0,tolerance:.00001f,channelMask:8).AssertEqual();
    }

    /// <summary>Filtering and correction match an independent double reference at borders and three storage scales.</summary>
    [Theory]
    [InlineData(.000244140625f)]
    [InlineData(1f)]
    [InlineData(64f)]
    public void FilterMatchesPhysicalReferenceAcrossStorageScales(float storage)
    {
        EnsureShaderTestAvailable();
        // Arrange
        Vector3[] colors = Enumerable.Range(0, 128).Select(i => new Vector3(.00003f * MathF.Pow(2, (i % 16) * .15f), .00002f, .00001f)).ToArray();
        // Act
        using var result = Pipeline(colors, 16, 8, storage, 13, new(.4f, .7f), new(3, 2));
        double[] logs = colors.Select(Luminance).Select(Math.Log2).ToArray();
        var expectedLogs=new double[colors.Length];
        var expectedBaseValues=new double[colors.Length];
        var expectedStops=new double[colors.Length];
        var expectedGain=new double[colors.Length*3];
        for (int i = 0; i < colors.Length; i++)
        {
            double expectedBase = Filter(logs, 16, 8, i, 1);
            double stops = Stops(expectedBase, 13, new(.4f, .7f), new(3, 2));
            expectedLogs[i]=logs[i];expectedBaseValues[i]=expectedBase;expectedStops[i]=stops;
            for (int channel = 0; channel < 3; channel++)
                expectedGain[i*3+channel]=Math.Pow(2,stops);
        }
        // Assert
        ImageAssertions.Comparisons.CompareComponentDoubles(result[0].Texture,expectedLogs,.000003,1).AssertEqual();
        ImageAssertions.Comparisons.CompareComponentDoubles(result[1].Texture,expectedBaseValues,.000005,1).AssertEqual();
        ImageAssertions.Comparisons.CompareComponentDoubles(result[2].Texture,expectedStops,.000005,8).AssertEqual();
        // Round each quotient to float before the original independent double-reference comparison.
        using var divisors=TestFramework.CreateTexture(16,8,TextureFormat.Rgba32f,colors.SelectMany(c=>new[]{c.X,c.Y,c.Z,1f}).ToArray());
        var transform = ImageAssertions.Projections;
        using var gains=transform.Apply(result[2].Texture, MaterialProjections.LocalExposurePrototypeTests_FilterMatchesPhysicalReferenceAcrossStorageScales,second:divisors);
        ImageAssertions.Comparisons.CompareComponentDoubles(gains,expectedGain,.00002,7).AssertEqual();
    }

    /// <summary>Black, negative, NaN and infinity samples are invalid and acquire no correction or borrowed illumination.</summary>
    [Fact]
    public void InvalidSamplesNeverBecomeLight()
    {
        EnsureShaderTestAvailable();
        // Arrange
        Vector3[] colors = [Vector3.Zero, new(-1, 1, 1), new(float.NaN), new(float.PositiveInfinity), new(.001f), new(.002f)];
        // Act
        using var result = Pipeline(colors, 6, 1, 1, 14, new(.5f), new(4));
        // Assert
        var invalid=new RHITextureRegion(new(0),0,0,0,4,1,1);
        ImageAssertions.Comparisons.CompareConstant(result[0].Texture,0,selection:invalid,channelMask:2).AssertEqual();
        ImageAssertions.Comparisons.CompareConstant(result[1].Texture,0,selection:invalid,channelMask:2).AssertEqual();
        ImageAssertions.Comparisons.CompareConstant(result[2].Texture,0,selection:invalid).AssertEqual();
        Assert.Equal(1,GpuLocalRead.Scalar(result[1].Texture,9));
    }

    #endregion
    #region Sampling and metadata admission
    /// <summary>Full-resolution guidance rejects an isolated bright source and normalizes reconstruction at clamped borders.</summary>
    [Fact]
    public void GuidedReconstructionRejectsBrightLeakage()
    {
        EnsureShaderTestAvailable();
        // Arrange
        using var texture = TestFramework.CreateTexture(2, 1, TextureFormat.Rg32f, [-16, 1, 8, 1]);
        // Act and assert each authored guidance row.
        foreach (float guidance in new[] { -16f, 8f })
        {
            float[] result = Draw(LocalExposureProbeOperation.GuidedSample, 1, 1, texture,
                new(.5f, .5f, guidance, 1), default);
            Assert.InRange(Math.Abs(result[0] - guidance), 0, .00001f);
            Assert.Equal(1, result[1]);
        }
        float[] border = Draw(LocalExposureProbeOperation.GuidedSample, 1, 1, texture, new(0, 0, -16, 1));
        Assert.Equal(-16, border[0]); Assert.Equal(1, border[1]);
        using var missing = TestFramework.CreateTexture(1, 1, TextureFormat.Rg32f, [0, 0]);
        float[] invalid = Draw(LocalExposureProbeOperation.GuidedSample, 1, 1, missing, new(.5f, .5f, -16, 1));
        Assert.Equal(0, invalid[1]); Assert.Equal(-16, invalid[0]);
    }

    /// <summary>Corrupted finite or nonfinite base metadata never contaminates neighboring valid samples.</summary>
    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(1e30f)]
    public void CorruptedBaseMetadataIsRejected(float corrupted)
    {
        EnsureShaderTestAvailable();
        // Arrange
        using var mixed = TestFramework.CreateTexture(3, 1, TextureFormat.Rg32f, [-16, 1, corrupted, 1, -16, 1]);
        // Act
        using var filtered = FilterLocalBase(mixed, 3, 1, 1);
        // Assert
        Assert.Equal(-16,GpuLocalRead.Scalar(filtered.Texture,0));Assert.Equal(1,GpuLocalRead.Scalar(filtered.Texture,1));
        Assert.Equal(0,GpuLocalRead.Scalar(filtered.Texture,3));Assert.Equal(-16,GpuLocalRead.Scalar(filtered.Texture,4));
        using var missing = TestFramework.CreateTexture(1, 1, TextureFormat.Rg32f, [corrupted, 1]);
        float[] guided = Draw(LocalExposureProbeOperation.GuidedSample, 1, 1, missing, new(.5f, .5f, -16, 1));
        Assert.Equal(-16, guided[0]); Assert.Equal(0, guided[1]);
    }

    /// <summary>Invalid candidate widths produce a missing base and neutral reconstruction rather than undefined weights.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void InvalidRangeWidthsAreNeutral(float width)
    {
        EnsureShaderTestAvailable();
        // Arrange
        using var valid = TestFramework.CreateTexture(1, 1, TextureFormat.Rg32f, [-16, 1]);
        // Act
        using var filtered = FilterLocalBase(valid, 1, 1, width);
        float[] result = Draw(LocalExposureProbeOperation.GuidedSample, 1, 1, valid, new(.5f, .5f, -16, width));
        // Assert
        Assert.Equal(new[] { 0f, 0f }, filtered.Texture.ReadPixelsRegion(0, 0, 1, 1));
        if (float.IsNaN(width))
        {
            // Failure status is the contract; do not make correctness depend on the out base value.
            Assert.Equal(0f, result[1]);
            Assert.Equal(new[] { 0f, 1f }, result[2..]);

            using var physical = TestFramework.CreateTexture(1, 1, TextureFormat.Rg32f, [1f, 1f]);
            float[] correction = Draw(LocalExposureProbeOperation.ApplyCorrection, 1, 1, physical,
                new(1, 0, width, 0), new(.5f, .5f, 4, 4), baseImage: valid);
            Assert.Equal(new[] { 0f, 0f, 0f, 0f }, correction);
        }
        else
        {
            Assert.Equal(new[] { -16f, 0f, 0f, 1f }, result);
        }
        // Production compute invocations only generate nonnegative in-range IDs, so retain this direct negative-coordinate helper observation.
        float[] outside = Draw(LocalExposureProbeOperation.FilterLocalBase, 1, 1, valid, new(-1, -1, 1, 0));
        Assert.Equal(new[] { 0f, 0f, 0f, 1f }, outside);
    }

    /// <summary>Malformed UV coordinates cannot overflow integer conversion or borrow edge illumination.</summary>
    [Fact]
    public void InvalidCoordinatesAreRejectedBeforeIntegerConversion()
    {
        EnsureShaderTestAvailable();
        // Arrange
        using var coordinates = TestFramework.CreateTexture(6, 1, TextureFormat.R32f, [-1, .5f, 2, 1e30f, float.NaN, float.PositiveInfinity]);
        using var valid = TestFramework.CreateTexture(1, 1, TextureFormat.Rg32f, [-16, 1]);
        // Act
        using var result = DrawImage(LocalExposureProbeOperation.GuidedCoordinate, 6, 1, coordinates,
            new(-16, 1, 0, 0), baseImage: valid);
        // Assert
        ImageAssertions.Predicates.Predicate(result.Texture, MaterialPredicates.LocalExposurePrototypeTests_InvalidCoordinatesAreRejectedBeforeIntegerConversion).AssertEqual();
    }

    /// <summary>Corrupted globalStops or base metadata cannot produce nonfinite correction under identity compression.</summary>
    [Fact]
    public void InvalidCameraInputsMetadataProducesNeutralCorrection()
    {
        EnsureShaderTestAvailable();
        // Arrange
        using var metadata = TestFramework.CreateTexture(6, 1, TextureFormat.R32f, [-151, 129, 1e30f, -1e30f, float.NaN, float.PositiveInfinity]);
        // Act
        using var result = DrawImage(LocalExposureProbeOperation.InvalidCorrectionMetadata, 6, 1, metadata);
        // Assert
        ImageAssertions.Comparisons.CompareVector(result.Texture,new(0,0,0,1)).AssertEqual();
    }

    /// <summary>A nonfinite display pivot cannot create local gain even with otherwise valid scene metadata.</summary>
    [Fact]
    public void InvalidDisplayPivotProducesNeutralCorrection()
    {
        EnsureShaderTestAvailable();
        // Arrange
        using var metadata=TestFramework.CreateTexture(3,1,TextureFormat.R32f,[float.NaN,float.PositiveInfinity,float.NegativeInfinity]);
        // Act
        using var result=DrawImage(LocalExposureProbeOperation.InvalidDisplayPivot,3,1,metadata);
        // Assert
        ImageAssertions.Comparisons.CompareVector(result.Texture,new(0,0,0,1)).AssertEqual();
    }

    /// <summary>Nontrivial guided interpolation agrees with normalized independent double spatial and range weights.</summary>
    [Fact]
    public void GuidedInterpolationMatchesIndependentWeights()
    {
        EnsureShaderTestAvailable();
        // Arrange
        using var values = TestFramework.CreateTexture(2, 2, TextureFormat.Rg32f, [-14, 1, -13, 1, -12, 1, -11, 1]);
        // Act
        float[] actual = Draw(LocalExposureProbeOperation.GuidedSample, 1, 1, values, new(.375f, .625f, -12.5f, 1));
        // Assert against independently accumulated double-precision weights.
        double numerator = 0, denominator = 0;
        for (int y = 0; y < 2; y++) for (int x = 0; x < 2; x++)
        {
            double sample = -14 + y * 2 + x, difference = sample + 12.5;
            double weight = (x == 0 ? .75 : .25) * (y == 0 ? .25 : .75) * Math.Exp(-difference * difference / 2);
            numerator += sample * weight; denominator += weight;
        }
        Assert.InRange(Math.Abs(actual[0] - numerator / denominator), 0, .000003);
        Assert.Equal(1, actual[1]);
    }

    #endregion
    #region Compression and display
    /// <summary>Corrections preserve the detail residual, obey independent bounds and join continuously across globalStops.</summary>
    [Fact]
    public void BoundsAndSmoothJoinPreserveBaseDetail()
    {
        EnsureShaderTestAvailable();
        // Arrange
        using var values = TestFramework.CreateTexture(9, 1, TextureFormat.R32f, [-100, -4, -1, -.0001f, 0, .0001f, 1, 4, 100]);
        // Act
        using var result = DrawImage(LocalExposureProbeOperation.BoundsAndDetail, 9, 1, values,
            new(0, 0, 0, 0), new(.5f, .25f, 3, 2));
        float[] samples = [-100, -4, -1, -.0001f, 0, .0001f, 1, 4, 100];
        var expectedStops=new double[samples.Length];
        for (int i = 0; i < samples.Length; i++)
            expectedStops[i]=Stops(samples[i],0,new(.5f,.25f),new(3,2));
        // Assert
        ImageAssertions.Statistics.Inspect(result.Texture,-2,3,channelMask:1).AssertFiniteRange();
        ImageAssertions.Comparisons.CompareComponentDoubles(result.Texture,expectedStops,.00001,1).AssertEqual();
        ImageAssertions.Predicates.Predicate(result.Texture, MaterialPredicates.LocalExposurePrototypeTests_BoundsAndSmoothJoinPreserveBaseDetail).AssertEqual();
        Assert.InRange(Math.Abs(GpuLocalRead.Scalar(result.Texture,3*4)-GpuLocalRead.Scalar(result.Texture,5*4)),0,.0002);
        using var identity = DrawImage(LocalExposureProbeOperation.Correction, 9, 1, values,
            new(0, 0, 0, 0), new(1, 1, 8, 8));
        ImageAssertions.Comparisons.CompareConstant(identity.Texture,0,channelMask:1).AssertEqual();
        float[] hardLimit = Draw(LocalExposureProbeOperation.HardLimit, 1, 1, values);
        Assert.Equal(8, hardLimit[0]); Assert.Equal(-8, hardLimit[1]);
    }

    /// <summary>A synthetic 434-to-one lamp/terrain scene demonstrates bounded display compression with the unchanged cached LUT.</summary>
    [Fact]
    public void SyntheticLampTerrainComparisonUsesExistingFilmicLookup()
    {
        EnsureShaderTestAvailable();
        // Arrange
        Vector3 terrain = new(.00006592794125f, .0000526507864f, .00001556631946f), lamp = terrain * 434;
        Vector3[] colors = Enumerable.Range(0, 128).Select(i => i % 16 < 8 ? terrain : lamp).ToArray();
        float globalStops = -(float)((Math.Log2(Luminance(terrain)) + Math.Log2(Luminance(lamp))) / 2);
        using var local = Pipeline(colors, 16, 8, 1, globalStops, new(.5f), new(4));
        Vector4 controls = new(1, 1, 1.111798286f, .143282309f);
        using var lookup = new ColorGradingLut();
        var graded = lookup.Prepare(Programs.Api, controls);
        using var baselineInput = TestFramework.CreateTexture(16, 8, TextureFormat.Rgba32f, colors.SelectMany(c => new[] { c.X, c.Y, c.Z, 1f }).ToArray());
        // Preserve the direct pre-dither LUT observation: pbr_final adds display dithering and cannot satisfy this authored tolerance.
        using var baseline = DrawImage(LocalExposureProbeOperation.GradingLookup, 16, 8, baselineInput,
            new(64, 0, 0, 0), lookup: graded);
        using var candidateInput = local[2].ToPlane();
        // Act by passing baseline and local-corrected pixels through the same cached grading lookup.
        using var candidate = DrawImage(LocalExposureProbeOperation.GradingLookup, 16, 8, candidateInput,
            new(64, 0, 0, 0), lookup: graded);
        // Assert against the original two selected color tuples and the unchanged cached LUT.
        foreach (int pixel in new[] { 3, 12 })
        {
            // The independent grading reference needs only the two originally nominated RGB tuples.
            float[] corrected=local[2].Texture.ReadPixelsRegion(pixel,0,1,1);
            Vector3 physical = new(corrected[0],corrected[1],corrected[2]);
            Vector3 expected = FilmicGradingReference.Lookup(physical * 64, controls);
            for (int c = 0; c < 3; c++) Assert.InRange(Math.Abs(GpuLocalRead.Scalar(candidate.Texture,pixel*4+c) - expected[c]), 0, .001f);
            output.WriteLine($"Synthetic {(pixel == 3 ? "terrain" : "lamp")}: stops {corrected[3]:R}.");
        }
        Assert.True(GpuLocalRead.Scalar(candidate.Texture,3*4)>GpuLocalRead.Scalar(baseline.Texture,3*4));
        Assert.True(GpuLocalRead.Scalar(candidate.Texture,12*4)<GpuLocalRead.Scalar(baseline.Texture,12*4));
        Assert.Equal(1, lookup.Generation);
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
        programs.Prepare("predicate-material", 0, 0);
        programs.Prepare("projection-material", 0, 0);
    }
    #endregion

    #region Private
    #region GPU pipeline
    /// <summary>Returns one explicitly local probe tuple; image-domain callers retain a GPU snapshot instead.</summary>
    private float[] Draw(LocalExposureProbeOperation operation, int width, int height, DynamicTexture2D input,
        Vector4 parameters = default, Vector4 strengthsAndBounds = default, DynamicTexture2D? baseImage = null,
        DynamicTexture3D? lookup = null)
    {
        if (width != 1 || height != 1) throw new ArgumentException("Local tuple draws require one texel.");
        using var image = DrawImage(operation, width, height, input, parameters, strengthsAndBounds, baseImage, lookup: lookup);
        return image.Texture.ReadPixelsRegion(0,0,1,1);
    }
    /// <summary>Runs the shipped reduction and filter programs, then observes the direct correction helper.</summary>
    private GpuImageSet Pipeline(Vector3[] colors, int width, int height, float storage,
        float globalStops, Vector2 strengths, Vector2 bounds)
    {
        using var input = TestFramework.CreateTexture(width, height, TextureFormat.Rgba32f,
            colors.SelectMany(c => new[] { c.X * storage, c.Y * storage, c.Z * storage, 1f }).ToArray());
        using var logs = CreateFloatTexture(width, height, TextureFormat.Rg32f);
        using var baseOutput = CreateFloatTexture(width, height, TextureFormat.Rg32f);
        using var lighting = new TestFrameLightingPublication();
        lighting.Capture(storage);
        var reduceAsset = CreateReduceAsset();
        using var reduce = new LocalExposureReduceProgram(reduceAsset);
        Assert.True(reduce.EnsureReady());
        reduce.FrameLightingInputs = lighting.Buffer;
        reduce.SceneRadiance = input;
        reduce.LogOutput = new(logs, ImageAccess.WriteOnly, Format: TextureFormat.Rg32f);
        reduce.Dispatch((width + 7) / 8, (height + 7) / 8);
        BarrierLocalExposureImages();
        using var filtered = RunFilter(logs, baseOutput, width, height, 1);
        using var color = DrawImage(LocalExposureProbeOperation.ApplyCorrection, width, height, input,
            new(storage, globalStops, 1, 0), new(strengths.X, strengths.Y, bounds.X, bounds.Y), baseOutput);
        return new([logs, baseOutput, color.Texture]);
    }

    /// <summary>Runs the declared direct-helper observation with captured resources and an owned graphics pipeline.</summary>
    private GpuImageSnapshot DrawImage(LocalExposureProbeOperation operation, int width, int height, DynamicTexture2D input,
        Vector4 parameters = default, Vector4 strengthsAndBounds = default, DynamicTexture2D? baseImage = null,
        TextureFormat format = TextureFormat.Rgba32f, DynamicTexture3D? lookup = null)
    {
        using var program = Programs.Create<LocalExposurePrototypeProbeProgram>();
        program.Capture(operation, parameters, strengthsAndBounds, input, baseImage, lookup);
        using var target = TestFramework.CreateTestGBuffer(width, height, format);
        using var draw = new PostprocessDraw();
        var pipeline = draw.Prepare(program, target.PostprocessPass());
        var commands = RHICommandContext.Current;
        using var boundary = commands.EnterBoundary(new("Tests.LocalExposurePrototype", true,
            graphics: [pipeline], operations: BoundaryOperations.RenderPasses));
        boundary.Run(() => draw.Submit(commands, pipeline, target.PostprocessPass(), program.CaptureBindings()));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        return new(target[0]);
    }

    /// <summary>Runs the production filter on a borrowed log grid and returns its complete output image.</summary>
    private GpuImageSnapshot FilterLocalBase(DynamicTexture2D logImage, int width, int height, float rangeWidth)
    {
        using var output = CreateFloatTexture(width, height, TextureFormat.Rg32f);
        using var filter = RunFilter(logImage, output, width, height, rangeWidth);
        return new(output);
    }

    /// <summary>Creates and submits the retained production filter owner for the exact supplied grid.</summary>
    private LocalExposureFilterProgram RunFilter(DynamicTexture2D logImage, DynamicTexture2D output,
        int width, int height, float rangeWidth)
    {
        var asset = CreateFilterAsset();
        var filter = new LocalExposureFilterProgram(asset);
        try
        {
            Assert.True(filter.EnsureReady());
            using var inputs = new LocalExposureInputs();
            inputs.Capture(rangeWidth);
            filter.Inputs = inputs;
            filter.LogImage = logImage;
            filter.BaseOutput = new(output, ImageAccess.WriteOnly, Format: TextureFormat.Rg32f);
            filter.Dispatch((width + 7) / 8, (height + 7) / 8);
            BarrierLocalExposureImages();
            return filter;
        }
        catch
        {
            filter.Dispose();
            asset.Dispose();
            throw;
        }
    }

    /// <summary>Creates the registered production reduction executable asset.</summary>
    private ComputeShaderAsset CreateReduceAsset()
    {
        Assert.True(ComputeShaderAsset.TryCreateFromAssets(Programs.Api,
            new ShaderSettings(GpuShaderContracts.Registry.FindProgram(LocalExposureReduceProgram.Contract.Identity)),
            out var pipeline, out var infoLog), infoLog);
        return pipeline!;
    }

    /// <summary>Creates the registered production filter executable asset.</summary>
    private ComputeShaderAsset CreateFilterAsset()
    {
        Assert.True(ComputeShaderAsset.TryCreateFromAssets(Programs.Api,
            new ShaderSettings(GpuShaderContracts.Registry.FindProgram(LocalExposureFilterProgram.Contract.Identity)),
            out var pipeline, out var infoLog), infoLog);
        return pipeline!;
    }

    /// <summary>Creates one mutable RGBA or RG observation target for typed compute image writes.</summary>
    private static DynamicTexture2D CreateFloatTexture(int width, int height, TextureFormat format)
        => RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription
        {
            Dimension = TextureDimension.Two,
            Format = format,
            Width = width,
            Height = height,
            MipLevels = 1,
            Storage = ResourceStorageMode.Mutable,
            Filter = TextureFilterMode.Nearest,
            BaseLevel = 0,
            MaxLevel = 0,
            Label = "Tests.LocalExposurePrototype"
        }).RequireResource();

    /// <summary>Publishes image writes before their texture-fetch observations or graphics consumers.</summary>
    private static void BarrierLocalExposureImages()
        => RHICommandContext.Current.Barrier(new(ResourceAccess.Image | ResourceAccess.TextureFetch));

    #endregion
    #region Numerical references
    /// <summary>Computes physical luminance independently in double precision.</summary>
    private static double Luminance(Vector3 c) => .2126 * c.X + .7152 * c.Y + .0722 * c.Z;

    /// <summary>Evaluates the normalized truncated bilateral footprint without reproducing shader float arithmetic.</summary>
    private static double Filter(double[] logs, int width, int height, int pixel, double range)
    {
        int cx = pixel % width, cy = pixel / width;
        double numerator = 0, denominator = 0;
        for (int y = Math.Max(0, cy - 1); y <= Math.Min(height - 1, cy + 1); y++)
            for (int x = Math.Max(0, cx - 1); x <= Math.Min(width - 1, cx + 1); x++)
            {
                double difference = (logs[y * width + x] - logs[pixel]) / range;
                double weight = (x == cx ? 2 : 1) * (y == cy ? 2 : 1) * Math.Exp(-difference * difference / 2);
                numerator += logs[y * width + x] * weight; denominator += weight;
            }
        return numerator / denominator;
    }

    /// <summary>Independently evaluates piecewise smooth base compression and its engineering bounds.</summary>
    private static double Stops(double b, double globalStops, Vector2 strength, Vector2 bounds)
    {
        double relative = b + globalStops, t = Math.Clamp((relative + 1) / 2, 0, 1);
        double k = strength.X + (strength.Y - strength.X) * t * t * (3 - 2 * t);
        return Math.Clamp((k - 1) * relative, -bounds.Y, bounds.X);
    }

    #endregion

    #endregion
}
