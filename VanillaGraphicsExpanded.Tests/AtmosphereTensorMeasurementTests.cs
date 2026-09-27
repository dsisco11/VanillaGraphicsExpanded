using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using VanillaGraphicsExpanded.PBR.Atmosphere;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Records matched warmed scalar and TensorPrimitives atmospheric integration costs.</summary>
public sealed class AtmosphereTensorMeasurementTests
{
    #region Measurement
    /// <summary>Measures independent ABBA blocks without machine-specific performance assertions.</summary>
    [Fact]
    public void MatchedRadianceIntegration()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("VGE_RUN_ATMOSPHERE_MEASUREMENTS") == "1", "Explicit measurement run required.");
        Vector3[] directions = AtmosphereTensorTests.Directions(128);
        var output = new Vector3[directions.Length];
        var rows = new List<object>();
        foreach (Vector3 sun in new[] { Vector3.UnitY, Vector3.Normalize(new Vector3(1, .03f, 0)), -Vector3.UnitY })
        {
            // Warm both paths before every independent lighting workload, including tiered compilation.
            long warmStart = Stopwatch.GetTimestamp();
            do { Integrate(false, directions, sun, output); Integrate(true, directions, sun, output); }
            while (Stopwatch.GetElapsedTime(warmStart).TotalSeconds < 3);
            for (int block = 0; block < 4; block++)
            foreach (bool batch in new[] { false, true, true, false })
            {
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                for (int repeat = 0; repeat < 40; repeat++) Integrate(batch, directions, sun, output);
                double milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
                rows.Add(new { sun, block, batch, milliseconds, allocatedBytes = allocated, repeats = 40, directions = directions.Length });
                Assert.All(output, value => Assert.True(float.IsFinite(value.X + value.Y + value.Z)));
            }
        }
        string path = Environment.GetEnvironmentVariable("VGE_ATMOSPHERE_REPORT") ?? Path.Combine(AppContext.BaseDirectory, "atmosphere-tensor-measurement.json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(new { framework = RuntimeInformation.FrameworkDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(), cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"), processors = Environment.ProcessorCount, affinity = Process.GetCurrentProcess().ProcessorAffinity.ToInt64(), tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"), altitudeKm = 2, aerosol = 1, warmupSecondsPerWorkload = 3, rows }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        Xunit.TestContext.Current.TestOutputHelper!.WriteLine(path);
    }
    #endregion

    #region Matched integration
    /// <summary>Uses identical normalized inputs, destinations and numerical integration budgets for both paths.</summary>
    private static void Integrate(bool batch, Vector3[] directions, Vector3 sun, Vector3[] destination)
    {
        if (batch) AtmosphereModel.RadianceBatch(directions, sun, 2, 1, destination);
        else for (int i = 0; i < directions.Length; i++) destination[i] = AtmosphereModel.Radiance(directions[i], sun, 2, 1);
    }
    #endregion
}
