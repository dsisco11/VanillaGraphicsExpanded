using System.Numerics;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks the production primary lattice against independent geometric and scalar density calculations.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class CloudPrimaryBudgetTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Empty gaps, native crossings and invalid leaves cannot allocate additional shading samples.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CompleteRayLatticeMatchesIndependentGeometry(bool detail, bool stochastic)
    {
        EnsureShaderTestAvailable();
        using var program = new CloudTraversalProbe(Programs.Api);
        int exactUniform = 0;
        foreach (int width in new[] { 1, 21, 81, 200 })
        {
            using var native = DynamicTexture2D.Create(width, width, PixelInternalFormat.Rgba32f);
            using var volume = new CloudVolumeParameters();
            foreach (var quality in new[] { CloudVolumeSettings.Default,
                CloudVolumeSettings.Default with { ViewRaySampleMax = 96, ViewRaySampleMin = 2, ViewBudgetDistance = 2000 },
                CloudVolumeSettings.Default with { ViewRaySampleMax = 384, ViewRaySampleMin = 1, ViewBudgetDistance = 50 },
                CloudVolumeSettings.Default with { ViewRaySampleMax = 4, ViewRaySampleMin = 4, ViewBudgetDistance = 10000 },
                CloudVolumeSettings.Default with { ViewRaySampleMax = 384, ViewRaySampleMin = 16, ViewBudgetDistance = 10000 } })
            {
                var settings = quality with { Detail = detail, DetailAmplitude = 0 };
                volume.CaptureMap(width, default, 1, settings);
                foreach (int pattern in new[] { 0, 1, 2, 3 })
                {
                    Vector4[] columns = Columns(width, pattern);
                    native.UploadDataImmediate(MemoryMarshal.Cast<Vector4, float>(columns.AsSpan()).ToArray());
                    foreach (var (origin, direction, limit) in Rays(width))
                    {
                        Vector4[] actual = Dispatch(program, volume, native, origin, direction, limit, stochastic);
                        var span = Span(width, origin, direction, limit);
                        int count = span is null ? 0 : Math.Min(settings.ViewRaySampleMax, Math.Max(settings.ViewRaySampleMin,
                            (int)Math.Floor(settings.ViewRaySampleMax * Math.Min(1, (span.Value.End - span.Value.Start) / settings.ViewBudgetDistance))));
                        Assert.Equal(count, actual.Length);
                        if (span is null) continue;
                        double ds = (span.Value.End - span.Value.Start) / count;
                        for (int i = 0; i < count; i++)
                        {
                            double t = span.Value.Start + (i + (stochastic ? .173 : .5)) * ds;
                            Assert.InRange(Math.Abs(actual[i].X - t), 0, Math.Max(.001, Math.Abs(t) * 2e-6));
                            Assert.InRange(Math.Abs(actual[i].Y - ds), 0, Math.Max(.001, ds * 2e-6));
                            Vector3 point = origin + direction * actual[i].X;
                            double density = Density(columns, width, point, detail);
                            Assert.InRange(Math.Abs(actual[i].Z - density), 0, 2e-6);
                        }
                        if(!detail)
                        {
                            double sampledTau=actual.Sum(value=>(double)value.Z*value.Y);
                            var intersections=CloudTraversalOracle.Intersections(columns,width,origin,direction,limit);
                            double analyticTau=intersections.Sum(value=>(value.Far-value.Near)*columns[value.Z*width+value.X].X/50.0);
                            double residual=Math.Abs(Math.Exp(-sampledTau)-Math.Exp(-analyticTau));
                            if(pattern==1&&Math.Abs(intersections.Sum(value=>value.Far-value.Near)-(span.Value.End-span.Value.Start))<1e-5)
                            {
                                Assert.InRange(residual,0,2e-5);exactUniform++;
                            }
                        }
                    }
                }
            }
        }
        if (!detail) Assert.True(exactUniform > 0, "Uniform complete-span fixtures must exercise analytic attenuation.");
    }
    #endregion

    #region Private
    #region GPU execution
    /// <summary>Reads actual production-helper positions and shared density without substituting CPU scheduling.</summary>
    private static Vector4[] Dispatch(CloudTraversalProbe program, CloudVolumeParameters volume, DynamicTexture2D native,
        Vector3 origin, Vector3 direction, float limit, bool stochastic)
    {
        Vector4[] records=program.Run(volume,native,origin,direction,limit,stochastic?5:4);
        int count=(int)records[402].X;Assert.InRange(count,0,384);
        return records.AsSpan(0,count).ToArray();
    }
    #endregion

    #region Independent scalar reference
    /// <summary>Clips horizontal slabs with double arithmetic, independently of all production traversal helpers.</summary>
    private static (double Start, double End)? Span(int width, Vector3 origin, Vector3 direction, float limit)
    {
        double start = 0, end = limit;
        foreach (var (o, d) in new[] { (origin.X, direction.X), (origin.Z, direction.Z) })
        {
            if (Math.Abs(d) < 1e-8) { if (o < 0 || o > width * 50) return null; continue; }
            double a = -o / (double)d, b = (width * 50 - o) / (double)d;
            start = Math.Max(start, Math.Min(a, b)); end = Math.Min(end, Math.Max(a, b));
        }
        return end > start ? (start, end) : null;
    }

    /// <summary>Evaluates native boxes and exposed-face erosion without GLSL, DDA or hierarchy reuse.</summary>
    private static double Density(Vector4[] columns, int width, Vector3 point, bool detail)
    {
        int x = (int)Math.Floor(point.X / 50), z = (int)Math.Floor(point.Z / 50);
        if (!Occupied(columns, width, x, z, point.Y)) return 0;
        Vector4 column = columns[z * width + x];
        double factor = 1;
        if (detail)
        {
            double edge = Math.Min(point.Y - column.Z * 50, column.W * 50 - point.Y);
            if (!Occupied(columns, width, x - 1, z, point.Y)) edge = Math.Min(edge, point.X - x * 50);
            if (!Occupied(columns, width, x + 1, z, point.Y)) edge = Math.Min(edge, (x + 1) * 50 - point.X);
            if (!Occupied(columns, width, x, z - 1, point.Y)) edge = Math.Min(edge, point.Z - z * 50);
            if (!Occupied(columns, width, x, z + 1, point.Y)) edge = Math.Min(edge, (z + 1) * 50 - point.Z);
            double u = Math.Clamp(edge / 2, 0, 1); factor = u * u * (3 - 2 * u);
        }
        return column.X / 50.0 * factor;
    }

    /// <summary>Rejects the complete invalid native tuple before testing its vertical support.</summary>
    private static bool Occupied(Vector4[] columns, int width, int x, int z, float y)
    {
        if (x < 0 || z < 0 || x >= width || z >= width) return false;
        Vector4 value = columns[z * width + x];
        return float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) && float.IsFinite(value.W)
            && value.X > 0 && value.W > value.Z && y >= value.Z * 50 && y <= value.W * 50;
    }
    #endregion

    #region Authored geometry
    /// <summary>Separates empty, constant, discontinuous, invalid and tiny-positive native inputs.</summary>
    private static Vector4[] Columns(int width, int pattern)
    {
        var columns = new Vector4[width * width];
        for (int i = 0; i < columns.Length; i++) columns[i] = pattern switch
        {
            0 => Vector4.Zero,
            1 => new(.2f, 0, -10, 10),
            2 => i % 3 == 0 ? new(.2f, 0, -1, 1) : new(.1f, 0, 2, 3),
            _ => (i % 4) switch { 0 => new(float.NaN, 0, -1, 1), 1 => new(.2f, float.PositiveInfinity, -1, 1),
                2 => new(.2f, 0, 1, -1), _ => new(1e-12f, 0, -1, 1) }
        };
        return columns;
    }

    /// <summary>Includes camera-inside, very short foreground, zero span, parallel, negative and boundary-tied rays.</summary>
    private static IEnumerable<(Vector3 Origin, Vector3 Direction, float Limit)> Rays(int width)
    {
        float extent = width * 50;
        yield return (new(25, 0, 25), Vector3.UnitX, 100000);
        yield return (new(-25, 0, 25), Vector3.UnitX, 100000);
        yield return (new(extent + 25, 0, 25), -Vector3.UnitX, 100000);
        yield return (new(25, 0, 25), Vector3.UnitZ, .01f);
        yield return (new(25, 0, 25), Vector3.UnitZ, 25);
        yield return (new(25, 0, 25), Vector3.UnitZ, 0);
        yield return (new(25, 0, 25), Vector3.UnitY, 1000);
        yield return (new(-1, 0, 25), Vector3.UnitY, 1000);
        yield return (new(0, 0, 0), Vector3.Normalize(new(1, 0, 1)), 100000);
        yield return (new(extent, 0, extent), Vector3.Normalize(new(-1, 0, -1)), 100000);
        yield return (new(50, 0, 50), new(1e-10f, 0, 1), 100000);
    }
    #endregion
    #endregion
}
