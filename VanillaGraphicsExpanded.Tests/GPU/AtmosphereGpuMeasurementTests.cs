using System.Diagnostics;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Measures matched warmed atmospheric workloads without machine-dependent performance assertions.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class AtmosphereGpuMeasurementTests(HeadlessGLFixture fixture, ITestOutputHelper output) : RenderTestBase(fixture)
{
    #region Measurement
    /// <summary>Records ABBA CPU/GPU source rebuild and cached-source sky refresh costs, plus maximum-quality dispatch cost.</summary>
    [Fact]
    public void MatchedTransportCosts()
    {
        if (Environment.GetEnvironmentVariable("VGE_RUN_ATMOSPHERE_MEASUREMENTS") != "1")
            Assert.Skip("Set VGE_RUN_ATMOSPHERE_MEASUREMENTS=1 for atmospheric timing evidence.");
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        using var gpu = AtmosphereGpuComputation.Create(assets.Api);
        var cpu = new AtmosphereLookup();
        foreach (bool rebuild in new[] { true, false })
        {
            int cpuGeneration = 0, gpuGeneration = 0;
            // Both owners first receive the same state, keeping table reuse policy matched.
            cpu.Update(Vector3.UnitY, 0, 0); MeasureGpu(gpu, Vector3.UnitY, 0);
            for (int block = -1; block < 3; block++)
            {
                foreach (bool onGpu in new[] { false, true, true, false })
                {
                    int generation = onGpu ? ++gpuGeneration : ++cpuGeneration;
                    float clouds = rebuild ? ((generation & 1) == 0 ? .2f : .8f) : 0;
                    Vector3 sun = Vector3.Normalize(new Vector3(.2f + generation * .05f, 1, .1f));
                    if (onGpu)
                    {
                        var measured = MeasureGpu(gpu, sun, clouds);
                        if (block >= 0) output.WriteLine($"{(rebuild ? "source" : "cached")}, block={block}, GPU wall={measured.Wall:F3} ms, GPU elapsed={measured.Device:F3} ms, peak dispatch={measured.Peak:F3} ms, CPU update={measured.Cpu:F3} ms, updates={measured.Updates}");
                    }
                    else
                    {
                        var clock = Stopwatch.StartNew(); cpu.Update(sun, 0, clouds);
                        if (block >= 0) output.WriteLine($"{(rebuild ? "source" : "cached")}, block={block}, CPU wall={clock.Elapsed.TotalMilliseconds:F3} ms");
                    }
                }
            }
        }
        using var high = AtmosphereGpuComputation.Create(assets.Api);
        using var query = GpuTimerQuery.Create();
        query.Begin(); high.Update(Vector3.UnitY, 0, 0, 128, 96, 3); query.End();
        output.WriteLine($"Quality3 first bounded source dispatch: {query.GetResultNanoseconds() / 1e6:F3} ms");
        var highClock = Stopwatch.StartNew();
        AtmosphereLighting? completed = null;
        double maximumDispatch = 0, deviceTotal = 0;
        while (completed is null && highClock.Elapsed < TimeSpan.FromSeconds(30))
        {
            using var elapsed = GpuTimerQuery.Create();
            elapsed.Begin(); completed = high.Update(Vector3.UnitY, 0, 0, 128, 96, 3); elapsed.End();
            double milliseconds = elapsed.GetResultNanoseconds() / 1e6;
            deviceTotal += milliseconds; maximumDispatch = Math.Max(maximumDispatch, milliseconds);
        }
        Assert.NotNull(completed); Assert.Equal(128 * 96 * 4, completed.Sky.Length);
        Assert.All(completed.Sky, value => Assert.True(float.IsFinite(value)));
        output.WriteLine($"Quality3 remaining full generation: wall={highClock.Elapsed.TotalMilliseconds:F3} ms, device={deviceTotal:F3} ms, maximum dispatch={maximumDispatch:F3} ms");
    }

    /// <summary>Accumulates device query time per submitted update while waiting only on its real GPU dependency.</summary>
    private static (double Wall, double Device, double Peak, double Cpu, int Updates) MeasureGpu(AtmosphereGpuComputation gpu, Vector3 sun, float clouds)
    {
        var clock = Stopwatch.StartNew();
        double total = 0, peak = 0, cpu = 0;
        int updates = 0;
        while (clock.Elapsed < TimeSpan.FromSeconds(30))
        {
            using var query = GpuTimerQuery.Create();
            query.Begin(); long started = Stopwatch.GetTimestamp();
            var result = gpu.Update(sun, 0, clouds, 32, 24);
            cpu += Stopwatch.GetElapsedTime(started).TotalMilliseconds; updates++;
            query.End();
            double elapsed = query.GetResultNanoseconds() / 1e6;
            total += elapsed; peak = Math.Max(peak, elapsed);
            if (result is not null) return (clock.Elapsed.TotalMilliseconds, total, peak, cpu, updates);
        }
        throw new TimeoutException("Atmosphere measurement did not complete.");
    }
    #endregion
}
