using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.WorldPartition;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies compact readiness ownership across partial shifts, full wraps and stale uploads.</summary>
public sealed partial class LumOnNearFieldFunctionalTests
{
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
    [InlineData(0, 128, 64)]
    [InlineData(0, -128, 64)]
    [InlineData(0, 128, 48)]
    [InlineData(0, -128, 48)]
    public void RegionRing_PreservesOverlapAndClearsReassignedSlots(int axis, int distance, int resolution)
    {
        EnsureShaderTestAvailable();
        using var fixture = new NearFieldVoxelFixture(resolution: resolution);
        Assert.Equal(TextureFormat.Rgba8, fixture.Scene.Light.InternalFormat);
        Assert.Equal(TextureFormat.Rgba8, fixture.Scene.Materials.InternalFormat);
        Assert.Equal(TextureFormat.R8ui, fixture.Scene.Regions.InternalFormat);
        ImageAssertions.Inspect(fixture.Scene.Regions,0,0).AssertFiniteRange();
        fixture.Publish(new ControlledVoxelWorld());
        var previousOrigin = fixture.Scene.Origin;
        var center = new VectorInt3(axis == 0 || axis == 3 ? distance : 0,
            axis == 1 || axis == 3 ? distance : 0, axis == 2 || axis == 3 ? distance : 0);
        fixture.MoveCenter(center);
        int size = fixture.Scene.RegionResolution;
        var origin = fixture.Scene.Origin;
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
        ImageAssertions.CompareBits(fixture.Scene.Regions,expected.Select(value=>(uint)value).ToArray()).AssertEqual();

        // A current-version completion for an evicted region still cannot claim its reused slot.
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
        ImageAssertions.CompareBits(fixture.Scene.Regions,expected.Select(value=>(uint)value).ToArray()).AssertEqual();

        fixture.Publish(new ControlledVoxelWorld());
        ImageAssertions.Inspect(fixture.Scene.Regions,1,1).AssertFiniteRange();
        var traceOffset = new VectorInt3((int)Math.Ceiling(center.X / 32d) * 32,
            (int)Math.Ceiling(center.Y / 32d) * 32,
            (int)Math.Ceiling(center.Z / 32d) * 32);
        // Keep the clear segment inside this deliberately small ring; coverage policy is tested separately.
        var result = Trace(fixture, worldOffset: traceOffset, cacheSpacing: 2);
        ObserveTrace(result, "return bvec2(a.r>=9.99 && a.r<=10.01 && b.r==1.0,false);").AssertEqual();
    }

    /// <summary>Compact 3D uploads preserve non-four-byte row data and the caller's unpack alignment.</summary>
    [Fact]
    public void ReadinessUpload_UsesPackedBytesAndRestoresAlignment()
    {
        EnsureShaderTestAvailable();
        using var scene = new ControlledTraceGpuScene(48);
        var data = Enumerable.Range(0, 27).Select(i => (byte)(i % 3)).ToArray();
        GL.GetInteger(GetPName.UnpackAlignment, out int originalAlignment);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 8);
        try
        {
            scene.Regions.UploadDataImmediate(data, 0, 0, 0, 3, 3, 3);
            GL.GetInteger(GetPName.UnpackAlignment, out int alignment);
            Assert.Equal(8, alignment);
            ImageAssertions.CompareBits(scene.Regions,data.Select(value=>(uint)value).ToArray()).AssertEqual();
        }
        finally { GL.PixelStore(PixelStoreParameter.UnpackAlignment, originalAlignment); }
    }

    #endregion
}
