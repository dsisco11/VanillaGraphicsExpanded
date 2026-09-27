using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using VanillaGraphicsExpanded.PBR.Atmosphere;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Measures complete matched scalar and SIMD table builds without timing assertions.</summary>
public sealed class AtmosphereMultipleScatteringTensorMeasurementTests
{
    #region Measurement
    /// <summary>Records warmed independent ABBA blocks for the production default and bounded higher-quality budgets.</summary>
    [Fact]
    public void MatchedTableConstruction()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("VGE_RUN_ATMOSPHERE_MEASUREMENTS") == "1", "Explicit measurement run required.");
        var rows = new List<object>();
        for (int workload = -1; workload < 4; workload++)
        {
            var budget = AtmosphereScatteringBudget.FromQuality(Math.Max(0, workload));
            int width = workload < 0 ? budget.Width : 2;
            int height = workload < 0 ? budget.Height : 2;
            // Warm the same complete-table workload in both paths before retaining any timings.
            long warm = Stopwatch.GetTimestamp();
            do { Build(false, width, height, budget); Build(true, width, height, budget); }
            while (Stopwatch.GetElapsedTime(warm).TotalSeconds < 2);
            for (int block = 0; block < 3; block++)
            foreach (bool scalar in new[] { true, false, false, true })
            {
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                var table = Build(scalar, width, height, budget);
                double milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
                rows.Add(new { workload, width, height, budget, block, scalar, milliseconds, allocatedBytes = allocated });
                Assert.True(float.IsFinite(table.Sample(2, .5f).Length()));
            }
        }
        string path = Environment.GetEnvironmentVariable("VGE_ATMOSPHERE_REPORT") ?? Path.Combine(AppContext.BaseDirectory, "atmosphere-ms-tensor.json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(new { framework = RuntimeInformation.FrameworkDescription,
            cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"), affinity = Process.GetCurrentProcess().ProcessorAffinity.ToInt64(),
            tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"), rows }, new JsonSerializerOptions { WriteIndented = true }));
        Xunit.TestContext.Current.TestOutputHelper!.WriteLine(path);
    }
    #endregion

    #region Workload
    /// <summary>Uses the same table domain, atmosphere and quadrature budgets in both implementations.</summary>
    private static AtmosphereMultipleScattering Build(bool scalar, int width, int height, AtmosphereScatteringBudget budget) =>
        AtmosphereMultipleScattering.Build(1, sunSamples: width, altitudeSamples: height,
            directionSamples: budget.DirectionSamples, raySamples: budget.RaySamples, lightSamples: budget.LightSamples, useScalarReference: scalar);
    #endregion
}
