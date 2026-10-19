using System.Numerics;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks descriptor reuse and face pruning against independent support arithmetic in the shared sampled field.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class CloudDensityReuseTests(HeadlessGLFixture fixture, ITestOutputHelper output)
    : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>One shared program checks independent query batches rather than multiplying settings and scenes.</summary>
    [Fact]
    public void CachedAndPrunedDensityMatchesSharedFieldFormula()
    {
        EnsureShaderTestAvailable();
        using var program = new CloudTraversalProbe(Programs.Api);
        using var native = DynamicTexture2D.Create(3, 3, PixelInternalFormat.Rgba32f);
        using var volume = new CloudVolumeParameters();
        Vector4 normal = new(.2f, 0, -1, 2);
        Vector4[] columns =
        [
            normal, new(float.NaN, 0, -1, 2), new(1e-12f, 0, -1, 2),
            normal, new(.3f, 0, 0, 1), Vector4.Zero,
            normal, normal, normal
        ];
        native.UploadDataImmediate(MemoryMarshal.Cast<Vector4, float>(columns.AsSpan()).ToArray());
        float[] horizontal = [-1, 0, .25f, .5f, 1, 2, 10, 25, 40, 48, 49.5f, 50, 50.5f, 75, 99.5f, 100, 100.5f, 125, 149.5f, 150, 151];
        float[] vertical = [-1, 0, .25f, .5f, 1, 49, 49.5f, 50, 50.5f, 51, 99.5f, 100, 100.5f, 149.5f, 150, 151];
        var anchored = new CloudPlacement(120003, -84017, 0, 0, -13, 7, .25, .75, Vector3.Zero, 0);

        // Each batch adds one distinct branch: zero-amplitude detail, height-dependent shared faces,
        // nonzero anchored noise with wider erosion, then the detail-disabled units/validity path.
        foreach (var (name, origin, direction, positions, settings, placement, units) in new[]
        {
            ("zero-amplitude invalid/tiny/border", new Vector3(0, 0, .5f), Vector3.UnitX, horizontal,
                CloudVolumeSettings.Default with { DetailAmplitude = 0 }, default(CloudPlacement), 1f),
            ("same-cell changing neighbor height", new Vector3(49.5f, -50, 75), Vector3.UnitY, vertical,
                CloudVolumeSettings.Default, default(CloudPlacement), 1f),
            ("anchored noise and connected cell changes", new Vector3(0, 0, 125), Vector3.UnitX, horizontal,
                CloudVolumeSettings.Default with { DetailAmplitude = .95f, EdgeWidth = 10 }, anchored, .25f),
            ("detail-disabled validity and units", new Vector3(0, 0, .5f), Vector3.UnitX, horizontal,
                CloudVolumeSettings.Default with { Detail = false }, anchored, 4f)
        })
        {
            output.WriteLine($"Density equivalence: {name}; queries={positions.Length}");
            volume.CaptureMap(3, placement, units, settings);
            Vector4[] actual = program.Run(volume, native, origin, direction, 0, 11, positions);
            Assert.Equal(positions.Length, (int)actual[402].X);
            output.WriteLine(string.Join("; ", actual.Take(positions.Length).Select((value, index) =>
                $"{index}:t={positions[index]:G9},legacy={value.Y:G9},uncached={value.Z:G9},cached={value.W:G9}")));
            bool occupied = false;
            for (int i = 0; i < positions.Length; i++)
            {
                // The fixture computes unpruned support independently of descriptor reuse.
                // This is same-field arithmetic equality, not an approximation-quality comparison.
                float expected = actual[i].Y;
                Assert.True(float.IsFinite(expected) && expected >= 0);
                double tolerance = Math.Abs(expected) * 3e-6;
                Assert.InRange(Math.Abs((double)actual[i].Z - expected), 0, tolerance);
                Assert.InRange(Math.Abs((double)actual[i].W - expected), 0, tolerance);
                occupied |= expected > 0;
            }
            Assert.True(occupied, "Each query batch must exercise nonzero cloud density.");
            if (name == "zero-amplitude invalid/tiny/border")
            {
                Assert.Equal(0, actual[13].Y); // x=75: invalid native tuple.
                Assert.True(actual[17].W > 0); // x=125: tiny positive extinction survives the cache.
                Assert.Equal(0, actual[0].W); // No clamp-to-edge density outside the native map.
                Assert.Equal(0, actual[positions.Length - 1].W); // Finite-map exit cannot extend its last column.
            }
        }
    }
    #endregion
}
