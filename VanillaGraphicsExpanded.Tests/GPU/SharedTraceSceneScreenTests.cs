using System.Numerics;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises the actual screen-trace shader against production shared geometry publication.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GpuIntegration")]
public sealed class SharedTraceSceneScreenTests : NearFieldShaderTestBase
{
    /// <summary>Uses the exclusive material fixture's shared GPU context.</summary>
    public SharedTraceSceneScreenTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Shared screen tracing
    /// <summary>Captured cache lighting resolves shared geometry hits consistently at signed large coordinates, including valid darkness.</summary>
    [Theory]
    [InlineData(0, 0f, 32)] [InlineData(-16777216, .25f, 32)] [InlineData(16777216, .25f, 32)]
    [InlineData(-16777216, .25f, 64)] [InlineData(-16777216, .25f, 128)]
    public void SealedRoomUsesSharedHitLighting(int anchor, float light, int surfaceSize)
    {
        EnsureShaderTestAvailable();
        using var material = new ScopedPbrMaterialFixture();
        // Cache emission is authored independently of the obsolete voxel RGB field below.
        material.SetReadiness(true, true, emissive: light / 32f);
        var materials = new TraceGeometryMaterials(); uint id = materials.Resolve(material.Cube);
        var plan = TraceGeometryCoverage.Plan(new(anchor, 32, -5), true, surfaceSize, 256);
        using var shared = new SharedTraceGeometryFixture(plan, materials, (x, y, z) =>
        {
            bool wall = x == anchor - 3 || x == anchor + 3 || y == 29 || y == 35 || z == -8 || z == -2;
            return new(wall ? 2 | id << 2 : 1, 0, TraceGeometryVoxel.PackLight(new(light, light, light, 0)));
        });
        shared.Publish();
        using var cache = new SurfaceLightingScreenTraceFixture(shared.Scene,
            new(anchor - 3, 29, -8), new(anchor + 3, 35, -2));
        using var compatibility = new NearFieldVoxelFixture();
        var unavailable = Trace(compatibility, worldOffset: new(anchor, 32, 0), shared: shared.Scene);
        Assert.True(cache.CaptureAndSeed());
        var result = Trace(compatibility, worldOffset: new(anchor, 32, 0), shared: shared.Scene, surfaceLighting: cache.Snapshot);
        ObserveTrace(result, """
            uint unavailableFlags=floatBitsToUint(d.g);
            bool valid=all(equal(c.rgb,vec3(0))) && all(greaterThanEqual(a.rgb,vec3(multipliers.x)))
                && all(lessThanEqual(a.rgb,vec3(multipliers.y))) && d.r==0.0 && (unavailableFlags&1u)==1u
                && ((unavailableFlags>>16)&7u)==4u && (flags&1u)==1u
                && ((flags>>16)&7u)==uint(multipliers.z) && c.a==a.a && b.r==1.0 && (flags&32u)==0u;
            return bvec2(valid,false);
            """, unavailable,new(light-.002f,light+.002f,light==0?3:2,0)).AssertEqual();
    }

    /// <summary>Unsupported, unpublished, exhausted and out-of-domain traces cannot become cache handoff.</summary>
    [Theory]
    [InlineData("unsupported")] [InlineData("missing")] [InlineData("budget")] [InlineData("outside")]
    public void IncompleteSharedTracesRemainUnavailable(string scenario)
    {
        EnsureShaderTestAvailable();
        var plan = TraceGeometryCoverage.Plan(new(0, 32, -5), true, null, 256);
        using var shared = new SharedTraceGeometryFixture(plan, new(), (_, _, _) => new(scenario == "unsupported" ? 3u : 1u, 0, 0));
        if (scenario != "missing") shared.Publish();
        using var compatibility = new NearFieldVoxelFixture();
        var result = Trace(compatibility, budget: scenario == "budget" ? 1 : 256,
            worldOffset: new(0, scenario == "outside" ? 96 : 32, 0), shared: shared.Scene);
        ObserveTrace(result, "return bvec2(a.r==0.0 && b.r==0.0 && (flags&34u)==0u,false);").AssertEqual();
    }
    #endregion
}
