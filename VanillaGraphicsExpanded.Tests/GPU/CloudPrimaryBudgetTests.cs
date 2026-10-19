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
public sealed class CloudPrimaryBudgetTests(HeadlessGLFixture fixture, ITestOutputHelper output) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Each authored ray isolates a finite-lattice boundary without multiplying unrelated settings.</summary>
    [Fact]
    public void CompleteRayLatticeMatchesIndependentGeometry()
    {
        EnsureShaderTestAvailable();
        using var program = new CloudTraversalProbe(Programs.Api);
        // Compile the shared program once; each named ray still has isolated input ownership.
        foreach (string scenario in new[]
        {
            "minimum-short-span", "scaled-interior-count", "maximum-count", "fixed-four-count",
            "empty-columns", "separated-heights", "invalid-and-tiny-columns", "outside-entry",
            "negative-entry", "zero-span", "parallel-inside", "parallel-outside", "positive-corner",
            "negative-corner", "near-parallel-boundary", "blue-noise-offset", "detail-exposed-face",
            "detail-connected-face", "layer-entry", "above-layer", "inside-layer", "near-horizontal-entry",
            "missing-bounds", "stale-bounds"
        })
        {
            output.WriteLine($"Primary lattice: {scenario}");
            var test = CreateCase(scenario);
            using var native = DynamicTexture2D.Create(test.Width, test.Width, PixelInternalFormat.Rgba32f);
            using var volume = new CloudVolumeParameters();
            volume.CaptureMap(test.Width, default, 1, test.Settings);
            Vector4[] columns = Columns(test.Width, test.Pattern);
            native.UploadDataImmediate(MemoryMarshal.Cast<Vector4, float>(columns.AsSpan()).ToArray());
            using var hierarchy=test.Width>=20?new CloudHierarchy(()=>CloudHierarchyComputeShader.Create(Programs.Api)):null;
            DynamicTexture2D? bounds=null;
            if(hierarchy is not null)
            {
                var input=new CloudMapInput(this,1,1,1,1,1,1,new(native.TextureId,2,3,4,test.Width,50,5),default);
                Assert.True(hierarchy.TryBuild(input,()=>true),hierarchy.Failure);
                if(scenario=="stale-bounds")Assert.False(hierarchy.TryGetCurrent(input with {Generation=2},out bounds));
                else if(scenario!="missing-bounds")Assert.True(hierarchy.TryGetCurrent(input,out bounds));
            }

            // Compare the production helper with independently clipped geometry and scalar density.
            Vector4[] records=program.Run(volume,native,test.Origin,test.Direction,test.Limit,test.Stochastic?5:4,
                bounds:bounds,root:bounds is null?null:bounds.MipLevels-1);
            Vector4[] actual=records.AsSpan(0,(int)records[402].X).ToArray();
            var span = Span(test.Width, test.Origin, test.Direction, test.Limit);
            if(bounds is not null&&span is not null)span=LayerSpan(columns,test.Origin,test.Direction,span.Value);
            int count = span is null ? 0 : Math.Min(test.Settings.ViewRaySampleMax, Math.Max(test.Settings.ViewRaySampleMin,
                (int)Math.Floor(test.Settings.ViewRaySampleMax * Math.Min(1,
                    (span.Value.End - span.Value.Start) / test.Settings.ViewBudgetDistance))));
            Assert.Equal(count, actual.Length);
            if (span is null) continue;
            double ds = (span.Value.End - span.Value.Start) / count;
            for (int i = 0; i < count; i++)
            {
                double t = span.Value.Start + (i + (test.Stochastic ? .173 : .5)) * ds;
                Assert.InRange(Math.Abs(actual[i].X - t), 0, Math.Max(.001, Math.Abs(t) * 2e-6));
                Assert.InRange(Math.Abs(actual[i].Y - ds), 0, Math.Max(.001, ds * 2e-6));
                Vector3 point = test.Origin + test.Direction * actual[i].X;
                double density = Density(columns, test.Width, point, test.Settings.Detail);
                Assert.InRange(Math.Abs(actual[i].Z - density), 0, 2e-6);
                if (density > 0) Assert.True(actual[i].Z > 0, "Positive native extinction must not be classified as empty.");
            }

            // These horizontal rays are uniformly occupied throughout their complete clipped span.
            // Beer-Lambert is therefore exact; discontinuous/detail fixtures have no quality assertion.
            if (test.Pattern == 1 && !test.Settings.Detail && test.Direction.Y == 0)
            {
                double sampledTau = actual.Sum(value => (double)value.Z * value.Y);
                double analyticTau = (span.Value.End - span.Value.Start) * columns[0].X / 50.0;
                Assert.InRange(Math.Abs(Math.Exp(-sampledTau) - Math.Exp(-analyticTau)), 0, 2e-5);
            }
        }
    }
    #endregion

    #region Private
    #region Independent scalar reference
    /// <summary>Clips horizontal slabs with double arithmetic, independently of all production traversal helpers.</summary>
    private static (double Start, double End)? Span(int width, Vector3 origin, Vector3 direction, float limit)
    {
        double start = 0, end = limit;
        foreach (var (o, d) in new[] { (origin.X, direction.X), (origin.Z, direction.Z) })
        {
            if (d==0) { if (o < 0 || o > width * 50) return null; continue; }
            double a = -o / (double)d, b = (width * 50 - o) / (double)d;
            start = Math.Max(start, Math.Min(a, b)); end = Math.Min(end, Math.Max(a, b));
        }
        return end > start ? (start, end) : null;
    }
    /// <summary>Independently reduces valid authored heights and intersects their conservative envelope.</summary>
    private static (double Start,double End)? LayerSpan(Vector4[] columns,Vector3 origin,Vector3 direction,(double Start,double End) horizontal)
    {
        var occupied=columns.Where(c=>float.IsFinite(c.X)&&float.IsFinite(c.Y)&&float.IsFinite(c.Z)
            &&float.IsFinite(c.W)&&c.X>0&&c.W>c.Z).ToArray();
        if(occupied.Length==0)return null;
        double lo=occupied.Min(c=>(double)c.Z*50),hi=occupied.Max(c=>(double)c.W*50);
        double slack=8*1.1920928955078125e-7*Math.Max(1,Math.Max(Math.Max(Math.Abs(lo),Math.Abs(hi)),
            Math.Abs(origin.Y)+Math.Abs(direction.Y)*horizontal.End));
        if(direction.Y==0)return origin.Y>=lo-slack&&origin.Y<=hi+slack?horizontal:null;
        double a=(lo-slack-origin.Y)/direction.Y,b=(hi+slack-origin.Y)/direction.Y;
        double start=Math.Max(horizontal.Start,Math.Min(a,b)),end=Math.Min(horizontal.End,Math.Max(a,b));
        return end>start?(start,end):null;
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

    /// <summary>Stores one distinct GPU contract and its independent scalar inputs.</summary>
    private sealed record RayCase(int Width, int Pattern, Vector3 Origin, Vector3 Direction, float Limit,
        CloudVolumeSettings Settings, bool Stochastic = false);

    /// <summary>Authors representative geometry and budget boundaries instead of a Cartesian scene matrix.</summary>
    private static RayCase CreateCase(string scenario)
    {
        var settings = CloudVolumeSettings.Default with { Detail = false, DetailAmplitude = 0,StopTransmittance=0 };
        var inside = new Vector3(25, 0, 25);
        // Interior scaling deliberately uses a fractional result, distinguishing floor from rounding.
        return scenario switch
        {
            "layer-entry" => new(21,2,new(25,-200,25),Vector3.Normalize(new(0,1,1)),1500,settings),
            "above-layer" => new(21,2,new(25,300,25),Vector3.Normalize(new(0,-1,1)),1500,settings),
            "inside-layer" => new(21,2,inside,Vector3.Normalize(new(0,1,1)),1500,settings),
            "near-horizontal-entry" => new(21,2,new(25,-50.0001f,25),new(0,1e-9f,0),1e7f,settings),
            "missing-bounds" or "stale-bounds" => new(21,2,new(25,-200,25),Vector3.Normalize(new(0,1,1)),1500,settings),
            "minimum-short-span" => new(1, 1, inside, Vector3.UnitZ, .01f,
                settings with { ViewRaySampleMax = 384, ViewRaySampleMin = 16, ViewBudgetDistance = 10000 }),
            "scaled-interior-count" => new(21, 1, inside, Vector3.UnitX, 123,
                settings with { ViewRaySampleMax = 96, ViewRaySampleMin = 2, ViewBudgetDistance = 2000 }),
            "maximum-count" => new(200, 1, inside, Vector3.UnitX, 100000,
                settings with { ViewRaySampleMax = 384, ViewRaySampleMin = 1, ViewBudgetDistance = 50 }),
            "fixed-four-count" => new(21, 1, inside, Vector3.UnitZ, 25,
                settings with { ViewRaySampleMax = 4, ViewRaySampleMin = 4, ViewBudgetDistance = 10000 }),
            "empty-columns" => new(21, 0, inside, Vector3.UnitX, 100000, settings),
            "separated-heights" => new(21, 2, inside, Vector3.UnitX, 100000, settings),
            "invalid-and-tiny-columns" => new(21, 3, inside, Vector3.UnitX, 100000, settings),
            "outside-entry" => new(21, 1, new(-25, 0, 25), Vector3.UnitX, 100000, settings),
            "negative-entry" => new(21, 1, new(1075, 0, 25), -Vector3.UnitX, 100000, settings),
            "zero-span" => new(1, 1, inside, Vector3.UnitZ, 0, settings),
            "parallel-inside" => new(1, 1, inside, Vector3.UnitY, 1000, settings),
            "parallel-outside" => new(1, 1, new(-1, 0, 25), Vector3.UnitY, 1000, settings),
            "positive-corner" => new(21, 1, Vector3.Zero, Vector3.Normalize(new(1, 0, 1)), 100000, settings),
            "negative-corner" => new(21, 1, new(1050, 0, 1050), Vector3.Normalize(new(-1, 0, -1)), 100000, settings),
            "near-parallel-boundary" => new(21, 2, new(50, 0, 50), new(1e-10f, 0, 1), 100000, settings),
            "blue-noise-offset" => new(21, 2, inside, Vector3.UnitX, 100000, settings, true),
            "detail-exposed-face" => new(1, 1, new(.25f, 0, 25), Vector3.UnitY, 1,
                settings with { Detail = true }),
            "detail-connected-face" => new(21, 1, new(49.9f, 0, 25), Vector3.UnitX, .2f,
                settings with { Detail = true }),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown primary ray contract.")
        };
    }
    #endregion
    #endregion
}
