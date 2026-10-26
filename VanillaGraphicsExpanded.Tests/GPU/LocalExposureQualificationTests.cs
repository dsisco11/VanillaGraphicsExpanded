using VanillaGraphicsExpanded.Rendering.RHI;
using System.Diagnostics;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.PBR.Postprocessing.LocalExposure;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Separates bounded display-scene checks from explicitly requested full-resolution timing.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LocalExposureQualificationTests(HeadlessGLFixture fixture, ITestOutputHelper output) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Checks moving edges and palette changes against independent spatial reconstruction and display references.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepresentativeCurrentFrameSequenceMatchesReference(bool indirectSource)
    {
        EnsureShaderTestAvailable();
        const int width = 32, height = 16;
        float scale = indirectSource ? 1f / 16 : 1;
        using var lighting = new TestFrameLightingPublication(); lighting.Capture(scale);
        using var owner = new LocalExposureRenderer(); owner.FrameLightingInputs = lighting.Buffer;
        using var grading = new ColorGradingLut(); Vector4 controls = new(1, 1, 1, 0);
        var lookup = grading.Prepare(Programs.Api, controls);
        using var history = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = RHIBufferRole.ShaderStorage, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.DynamicDraw, Label = null }).RequireResource();
        using var zero = Scene([Vector3.Zero], 1, 1, scale);
        using var camera = TestFrameCamera.CreateIdentity(width, height);
        using var target = TestFramework.CreateTestGBuffer(width, height, PixelInternalFormat.Rgba32f);
        using var draw = new PostprocessDraw();
        var final = Programs.Create<FinalDisplayShaderProgram>(p => p.LocalExposureEnabled = true);
        final.FrameInputs = camera; final.FrameLightingInputs = lighting.Buffer; final.GradingLookup = lookup;
        final.BloomImage = zero; final.ShaftImage = zero; final.ExposureBuffer = history;
        var policy = Candidate(8, 4);
        // These are completed-scene inputs, not a simulation of world transport or either LumOn renderer.
        foreach (string scenario in new[] { "uniform-cave", "uniform-bright", "lamp-terrain", "window-edge", "moving-edge", "celestial-galaxy", "weather-palette", "underwater-palette", "illumination-change" })
        {
            Vector3[] pixels = MakeScene(scenario, width, height, indirectSource);
            double metered = pixels.Select(Luminance).Where(v => v > 0).Average(v => Math.Log2(v));
            float ev = scenario == "uniform-bright" ? -4 : 6;
            history.UploadData(new[] { ev, 0f, (float)metered, 1f, 20f, 1f, 0f, 0f });
            using var source = Scene(pixels, width, height, scale);
            var baseImage = owner.Render(Programs.Api, source, policy);
            double[] reference = ReferenceBase(pixels, width, height, 8, 4);
            float[] actualBase = baseImage.ReadPixels();
            for (int i = 0; i < reference.Length; i++) Assert.InRange(Math.Abs(actualBase[i * 2] - reference[i]), 0, .00003);
            final.SceneImage = source; final.LocalBase = baseImage;
            final.Capture(false, new(controls, Vector4.Zero, Vector4.Zero), new(ev, 1, 0, MathF.Log2(.18f))); final.CaptureLocal(policy);
            var pipeline = draw.Prepare(final,target.PostprocessPass());
            Submit(draw, pipeline, target);
            float[] actual = target[0].ReadPixels(); double worst = 0, minStops = 0, maxStops = 0;
            double[] corrections = new double[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                double q = Math.Log2(Luminance(pixels[i]));
                double b = ReferenceSample(reference, 8, 4, (i % width + .5) / width, (i / width + .5) / height, q);
                double stops = Math.Clamp((Math.Log2(.18)-ev-b) * .5, -4, 4);
                corrections[i] = stops;
                minStops = Math.Min(minStops, stops); maxStops = Math.Max(maxStops, stops);
                Vector3 expected = FilmicGradingReference.Lookup(pixels[i] * (float)Math.Pow(2, ev + stops), controls);
                for (int c = 0; c < 3; c++)
                {
                    Assert.True(float.IsFinite(actual[i * 4 + c]));
                    double error = Math.Abs(actual[i * 4 + c] - FilmicGradingReference.Quantize(expected[c], i % width, i / width));
                    worst = Math.Max(worst, error);
                }
            }
            Assert.InRange(worst, 0, 1.001 / 255);
            if (scenario.StartsWith("uniform")) Assert.InRange(Math.Abs(corrections.Max()-corrections.Min()),0,.00001);
            QualifySpatialShape(scenario, pixels, corrections, width, height);
            output.WriteLine($"Synthetic {scenario}, logical {(indirectSource ? "indirect-added" : "direct-only")} source: worst display error={worst * 255:F3} codes, stops=[{minStops:F3},{maxStops:F3}]. No image history, physical source/curve unchanged.");
        }
        Assert.Equal(1, owner.Generation); Assert.Equal(1, grading.Generation);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Measures warmed production spatial ownership and final variants only when explicitly requested.</summary>
    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    public void WarmProductionCost(int width, int height)
    {
        if (Environment.GetEnvironmentVariable("VGE_RUN_LOCAL_EXPOSURE_MEASUREMENTS") != "1")
        { output.WriteLine("Full-resolution measurement not requested; set VGE_RUN_LOCAL_EXPOSURE_MEASUREMENTS=1."); return; }
        EnsureShaderTestAvailable();
        using var lighting = new TestFrameLightingPublication();
        using var owner = new LocalExposureRenderer(); owner.FrameLightingInputs = lighting.Buffer;
        using var source = TestFramework.CreateTexture(width, height, PixelInternalFormat.Rgba16f,
            MakeScene("window-edge", width, height, false).SelectMany(c => new[] { c.X, c.Y, c.Z, 1f }).ToArray());
        using var zero = Scene([Vector3.Zero], 1, 1);
        using var grading = new ColorGradingLut(); var lookup = grading.Prepare(Programs.Api, new(1, 1, 1, 0));
        using var history = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = RHIBufferRole.ShaderStorage, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.DynamicDraw, Label = null }).RequireResource(); history.UploadData(new[] { 6f, 0f, -12f, 1f, 10f, 1f, 0f, 0f });
        using var camera = TestFrameCamera.CreateIdentity(width, height);
        using var target = TestFramework.CreateTestGBuffer(width, height, PixelInternalFormat.Rgba16f);
        using var draw = new PostprocessDraw(); var policy = Candidate(256, 144);
        var spatial = owner.Render(Programs.Api, source, policy);
        var baseline = Programs.Create<FinalDisplayShaderProgram>();
        var final = Programs.Create<FinalDisplayShaderProgram>(p => p.LocalExposureEnabled = true);
        foreach (var program in new[] { baseline, final })
        {
            program.FrameInputs = camera; program.FrameLightingInputs = lighting.Buffer; program.SceneImage = source;
            program.BloomImage = zero; program.ShaftImage = zero; program.GradingLookup = lookup; program.ExposureBuffer = history;
            program.LocalBase = spatial;
            program.Capture(false, new(new(1, 1, 1, 0), Vector4.Zero, Vector4.Zero), new(6, 1, 0, MathF.Log2(.18f))); program.CaptureLocal(policy);
        }
        var globalPipeline = draw.Prepare(baseline,target.PostprocessPass()); var localPipeline = draw.Prepare(final,target.PostprocessPass());
        Action global = () => Submit(draw, globalPipeline, target);
        Action local = () => { owner.Render(Programs.Api, source, policy); Submit(draw, localPipeline, target); };
        Action finalOnly = () => Submit(draw, localPipeline, target);
        Action spatialOnly = () => owner.Render(Programs.Api, source, policy);
        Assert.True(GpuComputePipeline.TryCreateFromAssets(Programs.Api, LocalExposureReduceProgram.Contract.Identity, out var reducePipeline, out _, out _, preferSpirv: true));
        using var reduce = new LocalExposureReduceProgram(reducePipeline!); Assert.True(reduce.EnsureReady());
        Assert.True(GpuComputePipeline.TryCreateFromAssets(Programs.Api, LocalExposureFilterProgram.Contract.Identity, out var filterPipeline, out _, out _, preferSpirv: true));
        using var filter = new LocalExposureFilterProgram(filterPipeline!); Assert.True(filter.EnsureReady());
        using var inputs = new LocalExposureInputs(); inputs.Capture(1);
        reduce.FrameLightingInputs = lighting.Buffer; reduce.SceneRadiance = source;
        reduce.LogOutput = new(owner.Logarithms, TextureAccess.WriteOnly, Format: SizedInternalFormat.Rg32f);
        filter.Inputs = inputs; filter.LogImage = owner.Logarithms;
        filter.BaseOutput = new(spatial, TextureAccess.WriteOnly, Format: SizedInternalFormat.Rg32f);
        Action reduceOnly = () => { reduce.Dispatch(32, 18); RHICommandContext.Current.Barrier(new(RHIAccess.Image | RHIAccess.TextureFetch)); };
        Action filterOnly = () => { filter.Dispatch(32, 18); RHICommandContext.Current.Barrier(new(RHIAccess.Image | RHIAccess.TextureFetch)); };
        for (int i = 0; i < 3; i++) { global(); local(); } GL.Finish();
        var globalTimes = new List<(double Gpu, double Cpu)>(); var localTimes = new List<(double Gpu, double Cpu)>();
        for (int i = 0; i < 10; i++)
        {
            if ((i & 1) == 0) { globalTimes.Add(Measure(global)); localTimes.Add(Measure(local)); }
            else { localTimes.Add(Measure(local)); globalTimes.Add(Measure(global)); }
        }
        var spatialTimes = new List<(double Gpu, double Cpu)>(); var finalTimes = new List<(double Gpu, double Cpu)>();
        for (int i = 0; i < 10; i++) { spatialTimes.Add(Measure(spatialOnly)); finalTimes.Add(Measure(finalOnly)); }
        reduceOnly(); filterOnly();
        var reduceTimes = new List<(double Gpu, double Cpu)>(); var filterTimes = new List<(double Gpu, double Cpu)>();
        for (int i = 0; i < 10; i++) { reduceTimes.Add(Measure(reduceOnly)); filterTimes.Add(Measure(filterOnly)); }
        output.WriteLine($"LOCAL_COST {width}x{height}: global GPU={Median(globalTimes.Select(t => t.Gpu)):F4}ms CPU={Median(globalTimes.Select(t => t.Cpu)):F4}ms; local+final GPU={Median(localTimes.Select(t => t.Gpu)):F4}ms CPU={Median(localTimes.Select(t => t.Cpu)):F4}ms; added paired median={Median(localTimes.Select((t, i) => t.Gpu - globalTimes[i].Gpu)):F4}ms; spatial GPU={Median(spatialTimes.Select(t => t.Gpu)):F4}ms CPU={Median(spatialTimes.Select(t => t.Cpu)):F4}ms; optional-final GPU={Median(finalTimes.Select(t => t.Gpu)):F4}ms; local GPU range=[{localTimes.Min(t => t.Gpu):F4},{localTimes.Max(t => t.Gpu):F4}]ms; GPU={GL.GetString(StringName.Renderer)}; source/output RGBA16F; timestamps=10 alternating independent pairs; grid256x144 RG32F two images 589824 bytes; LUT warm; source upload/readback/query waits excluded from CPU. Synthetic input; game state unmodified; not total frame cost.");
        output.WriteLine($"LOCAL_STAGES {width}x{height}: reduce GPU={Median(reduceTimes.Select(t => t.Gpu)):F4}ms CPU={Median(reduceTimes.Select(t => t.Cpu)):F4}ms; filter GPU={Median(filterTimes.Select(t => t.Gpu)):F4}ms CPU={Median(filterTimes.Select(t => t.Cpu)):F4}ms. Independent typed dispatches, not additive to owner boundary timing.");
        Assert.Equal(1, owner.Generation); Assert.Equal(1, grading.Generation); Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Private
    #region GPU submission
    /// <summary>Creates explicitly stored physical RGB for completed-scene evaluation.</summary>
    private DynamicTexture2D Scene(Vector3[] pixels, int width, int height, float scale = 1) => TestFramework.CreateTexture(width, height,
        PixelInternalFormat.Rgba32f, pixels.SelectMany(c => new[] { c.X * scale, c.Y * scale, c.Z * scale, 1f }).ToArray());

    /// <summary>Submits the prepared production display executable through its restoring boundary.</summary>
    private static void Submit(PostprocessDraw draw, GraphicsPipeline pipeline, GpuFramebuffer target) =>
        Assert.True(GraphicsCommandContext.TryRun("Tests.LocalQualification.Final", [pipeline], true, commands => draw.Submit(commands, pipeline,target.PostprocessPass())));

    /// <summary>Uses independent timestamp objects around completed GPU work, excluding blocking result waits from host submission.</summary>
    private static (double Gpu, double Cpu) Measure(Action submit)
    {
        using var before = GpuTimestampQuery.Create(); using var after = GpuTimestampQuery.Create();
        before.Issue(); long start = Stopwatch.GetTimestamp(); submit(); double cpu = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        after.Issue(); return ((after.GetResultNanoseconds() - before.GetResultNanoseconds()) / 1e6, cpu);
    }
    #endregion
    #region Declared input scenarios
    /// <summary>Defines engineering coefficients without changing the selected runtime settings.</summary>
    private static LocalExposureParameters Candidate(int width, int height) => new(true, width, height, 1, .5f, .5f, 4, 4, false);

    /// <summary>Generates bounded spatial/palette inputs with capture-derived terrain and lamp anchors and explicit synthetic features.</summary>
    private static Vector3[] MakeScene(string scenario, int width, int height, bool indirect)
    {
        Vector3 terrain = new(.00006592794125f, .0000526507864f, .00001556631946f), lamp = new(.03469f, .02133f, .003867f);
        var pixels = new Vector3[width * height];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            float detail = 1 + .08f * MathF.Sin(x * .7f + y * .3f);
            Vector3 value = scenario switch
            {
                "uniform-cave" => new(MathF.Pow(2, -20)), "uniform-bright" => new(32),
                "lamp-terrain" => x > width * 3 / 4 ? lamp : terrain * detail,
                "window-edge" => x >= width / 2 ? new(20, 24, 30) : terrain * detail,
                "moving-edge" => x >= width / 2 + width / 8 ? new(20, 24, 30) : terrain * detail,
                "celestial-galaxy" => x == width / 3 && y == height / 3 ? new(100) : y < height / 2 ? new Vector3(.00012f, .00008f, .0002f) * detail : terrain * detail,
                "weather-palette" => new Vector3(.002f, .003f, .004f) * detail,
                "underwater-palette" => new Vector3(.00002f, .0008f, .0012f) * detail,
                "illumination-change" => terrain * (detail * 16), _ => throw new ArgumentOutOfRangeException(nameof(scenario))
            };
            // A logical indirect contribution is already composited here; this does not exercise LumOn transport.
            pixels[y * width + x] = value + (indirect && !scenario.StartsWith("uniform") ? terrain * .15f : Vector3.Zero);
        }
        return pixels;
    }
    #endregion
    #region Independent numerical references
    /// <summary>Returns the center pair of sorted observations.</summary>
    private static double Median(IEnumerable<double> samples) { double[] sorted = samples.Order().ToArray(); return (sorted[(sorted.Length - 1) / 2] + sorted[sorted.Length / 2]) * .5; }
    /// <summary>Evaluates physical photopic RGB luminance in double precision.</summary>
    private static double Luminance(Vector3 color) => .2126 * color.X + .7152 * color.Y + .0722 * color.Z;
    /// <summary>Independently area-reduces source logs and normalizes a bounded bilateral stencil.</summary>
    private static double[] ReferenceBase(Vector3[] source, int width, int height, int columns, int rows)
    {
        double[] logs = new double[columns * rows], result = new double[logs.Length];
        for (int y = 0; y < rows; y++) for (int x = 0; x < columns; x++)
        {
            double sum = 0; int count = 0;
            for (int sy = y * height / rows; sy < (y + 1) * height / rows; sy++) for (int sx = x * width / columns; sx < (x + 1) * width / columns; sx++)
            { sum += Math.Log2(Luminance(source[sy * width + sx])); count++; }
            logs[y * columns + x] = sum / count;
        }
        for (int y = 0; y < rows; y++) for (int x = 0; x < columns; x++)
        {
            double sum = 0, weight = 0;
            for (int ny = Math.Max(0, y - 1); ny <= Math.Min(rows - 1, y + 1); ny++) for (int nx = Math.Max(0, x - 1); nx <= Math.Min(columns - 1, x + 1); nx++)
            {
                double delta = logs[ny * columns + nx] - logs[y * columns + x];
                double w = (nx == x ? 2 : 1) * (ny == y ? 2 : 1) * Math.Exp(-delta * delta * .5);
                sum += logs[ny * columns + nx] * w; weight += w;
            }
            result[y * columns + x] = sum / weight;
        }
        return result;
    }
    /// <summary>Independently reconstructs the base with pixel-luminance-guided bilinear weights and normalized borders.</summary>
    private static double ReferenceSample(double[] source, int width, int height, double u, double v, double guidance)
    {
        double gx = u * width - .5, gy = v * height - .5, fx = gx - Math.Floor(gx), fy = gy - Math.Floor(gy), sum = 0, weights = 0;
        for (int y = 0; y < 2; y++) for (int x = 0; x < 2; x++)
        {
            double sample = source[Math.Clamp((int)Math.Floor(gy) + y, 0, height - 1) * width + Math.Clamp((int)Math.Floor(gx) + x, 0, width - 1)];
            double delta = sample - guidance, w = (x == 0 ? 1 - fx : fx) * (y == 0 ? 1 - fy : fy) * Math.Exp(-delta * delta * .5);
            sum += sample * w; weights += w;
        }
        return weights > 0 ? sum / weights : guidance;
    }
    /// <summary>Checks the reference's edge and detail behavior independently of implementation agreement.</summary>
    private static void QualifySpatialShape(string scenario, Vector3[] pixels, double[] corrections, int width, int height)
    {
        if (scenario is "window-edge" or "moving-edge")
        {
            int edge = scenario == "window-edge" ? width / 2 : width / 2 + width / 8;
            int row = height / 2;
            // Flat bright side must not acquire a halo/ringing band from the dark side.
            double[] bright = Enumerable.Range(edge, width - edge).Select(x => corrections[row * width + x]).ToArray();
            Assert.InRange(bright.Max() - bright.Min(), 0, .00001);
            // Fine authored contrast on the dark side must survive the broad-base compression.
            double[] before = Enumerable.Range(0, edge).Select(x => Math.Log2(Luminance(pixels[row * width + x]))).ToArray();
            double[] after = before.Select((q, x) => q + corrections[row * width + x]).ToArray();
            Assert.InRange((after.Max() - after.Min()) / (before.Max() - before.Min()), .65, 1.35);
            // The adjacent dark receiver must not be brightened relative to its plateau by an edge halo.
            Assert.InRange(corrections[row * width + edge - 1] - corrections[row * width + 2], -.12, .12);
        }
        if (scenario == "celestial-galaxy")
        {
            int star = height / 3 * width + width / 3;
            // Isolated high radiance cannot create a positive local-brightening halo in its neighbors.
            Assert.True(corrections[star - 1] <= corrections[star - 4] + .12);
            Assert.True(corrections[star + 1] <= corrections[star + 4] + .12);
        }
    }
    #endregion
    #endregion
}
