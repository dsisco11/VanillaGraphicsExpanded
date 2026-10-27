using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Compares complete horizon images and warm submission costs against the frozen pre-optimization shader.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class AmbientOcclusionPerformanceTests(HeadlessGLFixture fixture, ITestOutputHelper log) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Covers odd and collapsed mip axes, receiver selection, contact, corners, transmission and native quality.</summary>
    [Theory]
    [InlineData(1, 257, 1, 0)]
    [InlineData(33, 1, 2, 4)]
    [InlineData(65, 37, 1, 4)]
    [InlineData(65, 37, 2, 5)]
    [InlineData(257, 129, 1, 9)]
    [InlineData(257, 129, 2, 9)]
    [InlineData(67, 39, 2, 6)]
    [InlineData(67, 39, 2, 8)]
    [InlineData(67, 39, 2, 1)]
    [InlineData(67, 39, 2, 2)]
    [InlineData(67, 39, 2, 3)]
    [InlineData(67, 39, 2, 7)]
    [InlineData(67, 39, 2, 0)]
    [InlineData(65, 37, 1, 10)]
    [InlineData(65, 37, 2, 10)]
    public void CompleteHorizonImageMatchesReference(int width, int height, int quality, int scene)
    {
        EnsureShaderTestAvailable();
        Compare(width, height, quality, scene, false);
    }

    /// <summary>Qualifies complete images at representative display extents only when explicitly requested.</summary>
    [Theory]
    [Trait("Category", "AOFullResolution")]
    [InlineData(1280, 720, 2, 4)]
    [InlineData(1280, 720, 2, 5)]
    [InlineData(3440, 1377, 1, 4)]
    [InlineData(3440, 1377, 2, 4)]
    [InlineData(3440, 1377, 2, 5)]
    [InlineData(3440, 1377, 2, 9)]
    [InlineData(3440, 1377, 2, 0)]
    [InlineData(3440, 1377, 1, 10)]
    [InlineData(3440, 1377, 2, 10)]
    public void FullResolutionHorizonImageMatchesReference(int width, int height, int quality, int scene)
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("VGE_RUN_AO_FULL_RESOLUTION") == "1", "Explicit full-resolution AO comparison run required.");
        EnsureShaderTestAvailable();
        Compare(width, height, quality, scene, false);
    }

    /// <summary>Separates shared hierarchy work and readback from alternating warm horizon GPU and CPU measurements.</summary>
    [Theory]
    [InlineData(1280, 720, 2, 4)]
    [InlineData(1280, 720, 2, 5)]
    [InlineData(3440, 1377, 1, 4)]
    [InlineData(3440, 1377, 2, 4)]
    [InlineData(3440, 1377, 2, 5)]
    [InlineData(3440, 1377, 2, 9)]
    [InlineData(3440, 1377, 2, 0)]
    [InlineData(3440, 1377, 1, 10)]
    [InlineData(3440, 1377, 2, 10)]
    public void MatchedHorizonCosts(int width, int height, int quality, int scene)
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("VGE_RUN_AO_MEASUREMENTS") == "1", "Explicit AO measurement run required.");
        EnsureShaderTestAvailable();
        Compare(width, height, quality, scene, true);
    }

    /// <summary>Checks a captured scene against its original output before optionally measuring the restored horizon workload.</summary>
    [Fact]
    public void CapturedSceneMatchesOriginalAndReference()
    {
        string? directory = Environment.GetEnvironmentVariable("VGE_AO_CAPTURE_INPUT_DIR");
        Assert.SkipUnless(directory is not null, "Exported RenderDoc AO inputs required.");
        EnsureShaderTestAvailable();
        using var inputs = new CapturedAmbientOcclusionFixture(Programs, directory!);
        using var draw = new PostprocessDraw();
        using var referenceTarget = TestFramework.CreateTestGBuffer((inputs.Width + 1) / 2, (inputs.Height + 1) / 2, TextureFormat.Rgba16f);
        using var currentTarget = TestFramework.CreateTestGBuffer((inputs.Width + 1) / 2, (inputs.Height + 1) / 2, TextureFormat.Rgba16f);
        var actions = inputs.PrepareComparison(Programs.Create<AoHorizonReferenceProgram>(), draw, referenceTarget, currentTarget);
        actions.Reference();
        AssertSameImage(inputs.CapturedOutput, referenceTarget[0].ReadPixels());
        VerifyAndMeasure(actions, referenceTarget, currentTarget, inputs.Width, inputs.Height, inputs.Quality, -1,
            Environment.GetEnvironmentVariable("VGE_RUN_AO_MEASUREMENTS") == "1", "Restored captured scene at event 7091");
    }
    #endregion

    #region Private
    /// <summary>Uses identical HDR target formats and inputs, verifies every channel, then measures only prepared submissions.</summary>
    private void Compare(int width, int height, int quality, int scene, bool measure)
    {
        // A narrow vertical field of view makes the one-row case descend through coarser mips,
        // rather than merely covering collapsed axes at level zero.
        using var inputs = new AmbientOcclusionSceneFixture(TestFramework, Programs, width, height, scene,
            verticalFov: height == 1 ? 1 : 90,
            occluderTransmission: .3f, receiverTransmission: .2f);
        using var draw = new PostprocessDraw();
        using var referenceTarget = TestFramework.CreateTestGBuffer((width + 1) / 2, (height + 1) / 2, TextureFormat.Rgba16f);
        using var currentTarget = TestFramework.CreateTestGBuffer((width + 1) / 2, (height + 1) / 2, TextureFormat.Rgba16f);
        var actions = inputs.PrepareHorizonComparison(Programs.Create<AoHorizonReferenceProgram>(), draw, referenceTarget, currentTarget, quality);
        VerifyAndMeasure(actions, referenceTarget, currentTarget, width, height, quality, scene, measure, "Synthetic analytic scene");
    }

    /// <summary>Checks complete images, then records isolated warm submissions using the same procedure for both input sources.</summary>
    private void VerifyAndMeasure((Action Reference, Action Current) actions, GpuFramebuffer referenceTarget,
        GpuFramebuffer currentTarget, int width, int height, int quality, int scene, bool measure, string workload)
    {
        actions.Reference(); actions.Current();
        float[] expected = referenceTarget[0].ReadPixels(), actual = currentTarget[0].ReadPixels();
        AssertSameImage(expected, actual);
        Assert.All(actual, value => Assert.True(float.IsFinite(value)));
        if (!measure) return;

        var referenceGpu = new List<double>(); var currentGpu = new List<double>();
        var referenceCpu = new List<double>(); var currentCpu = new List<double>();
        // Warm both executables before alternating order; query collection occurs outside CPU submission timing.
        for (int warmup = 0; warmup < 3; warmup++) { actions.Reference(); actions.Current(); }
        for (int sample = 0; sample < 12; sample++)
        {
            if ((sample & 1) == 0) { Measure(actions.Reference, referenceGpu, referenceCpu); Measure(actions.Current, currentGpu, currentCpu); }
            else { Measure(actions.Current, currentGpu, currentCpu); Measure(actions.Reference, referenceGpu, referenceCpu); }
        }
        log.WriteLine($"{workload} horizon {width}x{height} quality={quality} scene={scene}: reference GPU median={Median(referenceGpu):F4}ms current={Median(currentGpu):F4}ms; reference CPU submission median={Median(referenceCpu):F4}ms current={Median(currentCpu):F4}ms; GPU={GL.GetString(StringName.Renderer)}. Identical full images; hierarchy, preparation, allocation and readback excluded. Not live frame costs.");
        log.WriteLine($"Reference GPU samples(ms): {string.Join(",", referenceGpu.Select(value => value.ToString("F4")))}; current: {string.Join(",", currentGpu.Select(value => value.ToString("F4")))}");
        string directory = Environment.GetEnvironmentVariable("VGE_AO_MEASUREMENT_DIR") ?? Path.Combine(AppContext.BaseDirectory, "ao-performance");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"horizon-{width}x{height}-q{quality}-scene{scene}.json"), JsonSerializer.Serialize(new
        {
            width, height, quality, scene, workload, capturedAtUtc = DateTime.UtcNow,
            renderer = GL.GetString(StringName.Renderer), version = GL.GetString(StringName.Version),
            description = "Isolated horizon-only draws with identical complete images; shared hierarchy, preparation, allocation, readback and query waits excluded. CPU includes distinct shader facades. Not live frame costs.",
            referenceGpuMilliseconds = referenceGpu, currentGpuMilliseconds = currentGpu,
            referenceCpuMilliseconds = referenceCpu, currentCpuMilliseconds = currentCpu
        }, new JsonSerializerOptions { WriteIndented = true }));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Times CPU enqueue work separately from the completed elapsed GPU query.</summary>
    private static void Measure(Action render, List<double> gpu, List<double> cpu)
    {
        using var timer = GpuTimerQuery.Create();
        timer.Begin();
        long start = Stopwatch.GetTimestamp();
        render();
        cpu.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        timer.End();
        gpu.Add(timer.GetResultNanoseconds() / 1e6);
    }

    /// <summary>Uses the midpoint of sorted samples rather than selecting the fastest run.</summary>
    private static double Median(List<double> samples) => samples.Order().Skip(samples.Count / 2 - 1).Take(2).Average();

    /// <summary>Compares channel bits, including signed zero, without dumping a multi-million-channel image on failure.</summary>
    private static void AssertSameImage(float[] expected, float[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        var expectedBits = MemoryMarshal.Cast<float, int>(expected.AsSpan());
        var actualBits = MemoryMarshal.Cast<float, int>(actual.AsSpan());
        if (expectedBits.SequenceEqual(actualBits)) return;
        int first = 0;
        while (expectedBits[first] == actualBits[first]) first++;
        Assert.Fail($"Horizon channel {first} differs: reference={expected[first]:R} (0x{expectedBits[first]:X8}), current={actual[first]:R} (0x{actualBits[first]:X8}).");
    }
    #endregion
}
