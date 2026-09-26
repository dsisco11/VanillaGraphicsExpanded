using System.Numerics;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises the actual screen-trace shader against production shared geometry publication.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GPU")]
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
        for (int i = 0; i < result.Radiance.Length; i += 4)
        {
            for (int channel = 0; channel < 3; channel++)
            {
                Assert.Equal(0f, unavailable.Radiance[i + channel]);
                Assert.InRange(result.Radiance[i + channel], light - .002f, light + .002f);
            }
            uint unavailableFlags = Flags(unavailable.Meta[i / 2 + 1]);
            uint readyFlags = Flags(result.Meta[i / 2 + 1]);
            Assert.Equal(0f, unavailable.Meta[i / 2]);
            Assert.Equal(1u, unavailableFlags & 1u);
            Assert.Equal(4u, (unavailableFlags >> 16) & 7u);
            Assert.Equal(1u, readyFlags & 1u);
            Assert.Equal(light == 0 ? 3u : 2u, (readyFlags >> 16) & 7u);
            Assert.Equal(unavailable.Radiance[i + 3], result.Radiance[i + 3]);
            Assert.Equal(1f, result.Meta[i / 2]);
            Assert.Equal(0u, Flags(result.Meta[i / 2 + 1]) & (1u << 5));
        }
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
        for (int i = 0; i < result.Radiance.Length; i += 4)
        {
            Assert.Equal(0f, result.Radiance[i]); Assert.Equal(0f, result.Meta[i / 2]);
            Assert.Equal(0u, Flags(result.Meta[i / 2 + 1]) & ((1u << 5) | (1u << 1)));
        }
    }
    #endregion
}
