using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Support.LumOn.WorldProbes.Fixtures;
using VanillaGraphicsExpanded.Tests.Component.LumOn.Fixtures;
using VanillaGraphicsExpanded.WorldPartition;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Verifies compact readiness ownership across partial shifts, full wraps and stale uploads.</summary>
public sealed partial class LumOnNearFieldFunctionalTests
{
    #region Public API
    #region Ring Ownership
    /// <summary>Moving the anchor preserves only overlapping ready regions and rejects evicted async completions.</summary>
    [Theory]
    [InlineData(0, 16, 64)]
    [InlineData(0, -16, 64)]
    [InlineData(1, 16, 64)]
    [InlineData(1, -16, 64)]
    [InlineData(2, 16, 64)]
    [InlineData(2, -16, 64)]
    [InlineData(3, 16, 48)]
    [InlineData(3, -16, 48)]
    [InlineData(0, 128, 48)]
    [InlineData(0, -128, 48)]
    public void RegionRing_PreservesOverlapAndClearsReassignedSlots(int axis, int distance, int resolution)
    {
        EnsureShaderTestAvailable();
        // Arrange: Publish every ring region and snapshot the original origin before shifting along this axis/distance.
        using var fixture = new NearFieldVoxelFixture(resolution: resolution);
        Assert.Equal(TextureFormat.Rgba8, fixture.Scene.Light.InternalFormat);
        Assert.Equal(TextureFormat.Rgba8, fixture.Scene.Materials.InternalFormat);
        Assert.Equal(TextureFormat.R8ui, fixture.Scene.Regions.InternalFormat);
        ImageAssertions.Statistics.Inspect(fixture.Scene.Regions,0,0).AssertFiniteRange();
        fixture.Publish(new ControlledVoxelWorld());
        var previousOrigin = fixture.Scene.Origin;
        var center = new VectorInt3(axis == 0 || axis == 3 ? distance : 0,
            axis == 1 || axis == 3 ? distance : 0, axis == 2 || axis == 3 ? distance : 0);
        fixture.MoveCenter(center);
        int size = fixture.Scene.RegionResolution;
        var origin = fixture.Scene.Origin;

        // Act: Compute the overlap expected after the shift and compare it with the actual readiness texture.
        var expected = new byte[size * size * size];
        for (int z = previousOrigin.Z; z < previousOrigin.Z + resolution; z += 16)
        for (int y = previousOrigin.Y; y < previousOrigin.Y + resolution; y += 16)
        for (int x = previousOrigin.X; x < previousOrigin.X + resolution; x += 16)
        {
            if (x < origin.X || x >= origin.X + resolution ||
                y < origin.Y || y >= origin.Y + resolution ||
                z < origin.Z || z >= origin.Z + resolution) continue;
            int sx = ((x / 16) % size + size) % size;
            int sy = ((y / 16) % size + size) % size;
            int sz = ((z / 16) % size + size) % size;
            expected[(sz * size + sy) * size + sx] = 1;
        }
        ImageAssertions.Comparisons.CompareBits(fixture.Scene.Regions,expected.Select(value=>(uint)value).ToArray()).AssertEqual();

        // A current-version completion for an evicted region still cannot claim its reused slot.
        // Assert: A completion for an evicted identity is rejected without changing the texture or revision.
        int oldX = distance > 0 ? previousOrigin.X : previousOrigin.X + resolution - 16;
        int oldY = distance > 0 ? previousOrigin.Y : previousOrigin.Y + resolution - 16;
        int oldZ = distance > 0 ? previousOrigin.Z : previousOrigin.Z + resolution - 16;
        var coordinate = new PartitionCoordinate(oldX / 16, oldY / 16, oldZ / 16);
        var key = new PartitionCellKey(fixture.Instance, "test", coordinate);
        long revision = fixture.Scene.Revision;
        var request = new PartitionRequest(1, key, 1, 1, long.MaxValue, CancellationToken.None);
        Assert.False(fixture.Scene.ClaimCell(request));
        Assert.False(fixture.Scene.PublishCell(request, new ControlledTraceVoxel[4096], new TraceGeometryMaterials()));
        Assert.Equal(revision, fixture.Scene.Revision);
        ImageAssertions.Comparisons.CompareBits(fixture.Scene.Regions,expected.Select(value=>(uint)value).ToArray()).AssertEqual();

        fixture.Publish(new ControlledVoxelWorld());
        ImageAssertions.Statistics.Inspect(fixture.Scene.Regions,1,1).AssertFiniteRange();

        // Act and assert: Trace within the small shifted ring and verify its complete clear segment remains valid.
        var traceOffset = new VectorInt3((int)Math.Ceiling(center.X / 32d) * 32,
            (int)Math.Ceiling(center.Y / 32d) * 32,
            (int)Math.Ceiling(center.Z / 32d) * 32);
        // Keep the clear segment inside this deliberately small ring; coverage policy is tested separately.
        var result = Trace(fixture, worldOffset: traceOffset, cacheSpacing: 2);
        ObserveTrace(result, SurfacePredicates.LumOnNearFieldFunctionalTestsCache_ParallaxNeighborhood_PreservesConstantRadiance).AssertEqual();
    }


    #endregion
    #endregion
}
