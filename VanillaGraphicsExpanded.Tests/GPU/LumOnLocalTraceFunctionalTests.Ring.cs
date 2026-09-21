using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Scene.LocalTracing;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.WorldPartition;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies compact readiness ownership across partial shifts, full wraps and stale uploads.</summary>
public sealed partial class LumOnLocalTraceFunctionalTests
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
    public void RegionRing_PreservesOverlapAndClearsReassignedSlots(int axis, int distance, int resolution)
    {
        EnsureShaderTestAvailable();
        using var fixture = new LocalTraceVoxelFixture(resolution: resolution);
        Assert.Equal(PixelInternalFormat.Rgba8, fixture.Scene.Light.InternalFormat);
        Assert.Equal(PixelInternalFormat.Rgba8, fixture.Scene.Materials.InternalFormat);
        Assert.Equal(PixelInternalFormat.R8ui, fixture.Scene.Regions.InternalFormat);
        Assert.All(ReadReadiness(fixture.Scene), ready => Assert.Equal(0, ready));
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
        Assert.Equal(expected, ReadReadiness(fixture.Scene));

        // A current-version completion for an evicted region still cannot claim its reused slot.
        int oldX = distance > 0 ? previousOrigin.X : previousOrigin.X + resolution - 16;
        int oldY = distance > 0 ? previousOrigin.Y : previousOrigin.Y + resolution - 16;
        int oldZ = distance > 0 ? previousOrigin.Z : previousOrigin.Z + resolution - 16;
        var coordinate = new PartitionCoordinate(oldX / 16, oldY / 16, oldZ / 16);
        var key = new PartitionCellKey(fixture.Instance, "test", coordinate);
        long revision = fixture.Scene.Revision;
        var request = new PartitionRequest(1, key, 1, 1, long.MaxValue, CancellationToken.None);
        Assert.False(fixture.Scene.ClaimCell(request));
        Assert.False(fixture.Scene.PublishCell(request, new LocalTraceSourceCell[4096], new LocalTraceMaterialRegistry()));
        Assert.Equal(revision, fixture.Scene.Revision);
        Assert.Equal(expected, ReadReadiness(fixture.Scene));

        fixture.Publish(new ControlledVoxelWorld());
        Assert.All(ReadReadiness(fixture.Scene), ready => Assert.Equal(1, ready));
        var traceOffset = new VectorInt3((int)Math.Ceiling(center.X / 32d) * 32,
            (int)Math.Ceiling(center.Y / 32d) * 32,
            (int)Math.Ceiling(center.Z / 32d) * 32);
        // Keep the clear segment inside this deliberately small ring; coverage policy is tested separately.
        var result = Trace(fixture, worldOffset: traceOffset, cacheSpacing: 2);
        for (int i = 0; i < result.Radiance.Length; i += 4)
        {
            Assert.InRange(result.Radiance[i], 9.99f, 10.01f);
            Assert.Equal(1, result.Meta[i / 2]);
        }
    }

    /// <summary>Compact 3D uploads preserve non-four-byte row data and the caller's unpack alignment.</summary>
    [Fact]
    public void ReadinessUpload_UsesPackedBytesAndRestoresAlignment()
    {
        EnsureShaderTestAvailable();
        using var scene = new LocalTraceGpuScene(48);
        var data = Enumerable.Range(0, 27).Select(i => (byte)(i % 3)).ToArray();
        GL.GetInteger(GetPName.UnpackAlignment, out int originalAlignment);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 8);
        try
        {
            scene.Regions.UploadDataImmediate(data, 0, 0, 0, 3, 3, 3);
            GL.GetInteger(GetPName.UnpackAlignment, out int alignment);
            Assert.Equal(8, alignment);
            Assert.Equal(data, ReadReadiness(scene));
        }
        finally { GL.PixelStore(PixelStoreParameter.UnpackAlignment, originalAlignment); }
    }

    /// <summary>Reads tightly packed integer readiness and restores external pixel-pack state.</summary>
    private static byte[] ReadReadiness(LocalTraceGpuScene scene)
    {
        var data = new byte[scene.RegionResolution * scene.RegionResolution * scene.RegionResolution];
        using var binding = GlStateCache.Current.BindTextureScope(TextureTarget.Texture3D, 0, scene.Regions.TextureId);
        GL.GetInteger(GetPName.PackAlignment, out int previousAlignment);
        GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
        try { GL.GetTexImage(TextureTarget.Texture3D, 0, PixelFormat.RedInteger, PixelType.UnsignedByte, data); }
        finally { GL.PixelStore(PixelStoreParameter.PackAlignment, previousAlignment); }
        return data;
    }
    #endregion
}
