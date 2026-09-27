using System.Diagnostics;
using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Records worker-side atmospheric build cost without machine-dependent timing assertions.</summary>
public sealed class AtmosphereMultipleScatteringMeasurementTests
{
    #region Measurement
    /// <summary>Separates angular and radial error at the high-altitude twilight boundary.</summary>
    [Fact]
    public void DiagnoseQuadrature()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("VGE_RUN_ATMOSPHERE_MEASUREMENTS") == "1", "Explicit measurement run required.");
        foreach (var budget in new[] { (32, 24), (32, 48), (64, 24), (128, 24), (128, 48), (256, 96) })
        {
            var table = AtmosphereMultipleScattering.Build(1, directionSamples: budget.Item1, raySamples: budget.Item2);
            foreach (float altitude in new[] { 2f, 99f })
            foreach (float sun in new[] { -.1f, 0f, 1f })
                Xunit.TestContext.Current.TestOutputHelper!.WriteLine($"directions={budget.Item1}, rays={budget.Item2}, altitude={altitude}, sun={sun}: {table.Sample(altitude, sun)}");
        }
    }
    /// <summary>Measures separate source-table construction and full sky snapshots reusing that table.</summary>
    [Fact]
    public void RecordWorkerCosts()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("VGE_RUN_ATMOSPHERE_MEASUREMENTS") == "1", "Explicit measurement run required.");
        AtmosphereMultipleScattering.Build(1);
        var lookup = new AtmosphereLookup();
        lookup.Update(Vector3.UnitY, 2, 0);
        foreach (string operation in new[] { "source table", "32x24 sky", "128x96 sky" })
        {
            var elapsed = new double[5];
            for (int run = 0; run < elapsed.Length; run++)
            {
                long start = Stopwatch.GetTimestamp();
                if (operation == "source table") AtmosphereMultipleScattering.Build(1);
                else
                {
                    int width = operation == "32x24 sky" ? 32 : 128;
                    Assert.True(lookup.Update(Vector3.Normalize(new Vector3(.1f * (run + 1), 1, 0)), 2, 0, width: width, height: width * 3 / 4));
                }
                elapsed[run] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }
            Array.Sort(elapsed);
            Xunit.TestContext.Current.TestOutputHelper!.WriteLine($"{operation}: median={elapsed[2]:F3} ms, range={elapsed[0]:F3}..{elapsed[^1]:F3} ms; {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        }
        var table = AtmosphereMultipleScattering.Build(1);
        Vector3[] directions = AtmosphereTensorTests.Directions(128), output = new Vector3[128];
        foreach (bool multiple in new[] { false, true, true, false })
        {
            for (int warm = 0; warm < 20; warm++) AtmosphereModel.RadianceBatch(directions, Vector3.UnitY, 2, 1, output, multiple ? table : null);
            long allocated = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
            for (int repeat = 0; repeat < 100; repeat++) AtmosphereModel.RadianceBatch(directions, Vector3.UnitY, 2, 1, output, multiple ? table : null);
            double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds / 100;
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Xunit.TestContext.Current.TestOutputHelper!.WriteLine($"128 directions multiple={multiple}: {elapsed:F3} ms, bytes={allocated}");
        }
    }
    #endregion
}
