using VanillaGraphicsExpanded.Tests.GPU.Assertions;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using VanillaGraphicsExpanded.WorldPartition;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises native GPU companion uploads and ring retirement through coordinator authorization.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class TraceGeometryPublicationTests : RenderTestBase
{
    /// <summary>Uses the existing headless GL owner.</summary>
    public TraceGeometryPublicationTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Publication scenarios
    /// <summary>Resolved face IDs and independent readiness flags survive budgeted table uploads before cells become ready.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MaterialTablesPublishCoherentlyUnderSmallBudget(bool hitReady)
    {
        EnsureContextValid();
        using var fixture = new ScopedPbrMaterialFixture();
        fixture.SetReadiness(true, hitReady);
        var materials = new TraceGeometryMaterials();
        uint id = materials.Resolve(fixture.Cube);
        var expected = materials.Snapshot();
        var coordinator = new PartitionCoordinator(new(1024, 256, 128, 128, 65536));
        var scene = new TraceGeometryGpuScene(48);
        var cache = new TraceGeometrySourceCache((key, version, _) => Task.FromResult<TraceGeometryChunk?>(
            new(key, version, Enumerable.Repeat(new TraceGeometryVoxel(2 | id << 2, id << 18, 0), 32768).ToArray())), _ => 0, _ => true);
        using var partition = new TraceGeometryPartition(coordinator, cache, materials, scene);
        var plan = TraceGeometryCoverage.Plan(new(0, 40, 0), true, null, 256);
        for (int i = 1; i <= 100; i++)
        {
            partition.Prepare(plan); coordinator.Pump(i);
            long bytes = scene.UploadedBytes;
            partition.Service(plan);
            Assert.InRange(scene.UploadedBytes - bytes, 0, 65536);
            if (scene.TablesRevision != expected.Revision)
                ImageAssertions.Inspect(scene.Readiness,0,0).AssertFiniteRange();
        }
        ImageAssertions.Inspect(scene.Readiness,1,1).AssertFiniteRange();
        uint[] faces=expected.Faces.ToArray();
        ImageAssertions.CompareBits(scene.Faces,faces).AssertEqual();
        Assert.NotEqual(0u, faces[id * 4]); Assert.Equal(hitReady ? 3u : 1u, faces[id * 4 + 3] & 3u);
        Assert.Equal((uint)fixture.Cube.Id,faces[id * 4 + 3] >> 2);
        byte[] colors=expected.Colors.ToArray();
        ImageAssertions.CompareRgba8(scene.Materials,colors).AssertEqual();
        if(hitReady)Assert.Equal((byte)MathF.Round(GgxHemisphereReference.CapturedDiffuse(1)*255),colors[id*48]);
        ImageAssertions.CompareBits(scene.Surfaces,expected.Surfaces.ToArray()).AssertEqual();
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Ready slots contain coherent geometry, packed/normalized light and initialized material flags.</summary>
    [Theory]
    [InlineData(0)] [InlineData(-16777216)] [InlineData(16777216)]
    public void SharedPublicationAndTeleportPreserveReadiness(int anchor)
    {
        EnsureContextValid();
        var coordinator = new PartitionCoordinator(new(1024, 256, 128, 128, 8 * 1024 * 1024));
        var scene = new TraceGeometryGpuScene(48);
        int version = 0;
        var cache = new TraceGeometrySourceCache((key, revision, _) => Task.FromResult<TraceGeometryChunk?>(
            new(key, revision, Enumerable.Repeat(new TraceGeometryVoxel(2, 23, 0x44332211), 32768).ToArray())), _ => version, _ => true);
        using var partition = new TraceGeometryPartition(coordinator, cache, new(), scene);
        var plan = TraceGeometryCoverage.Plan(new(anchor + 1, 40, anchor + 1), true, null, 256);
        for (int frame = 1; frame <= 10; frame++) { partition.Prepare(plan); coordinator.Pump(frame); partition.Service(plan); }
        ImageAssertions.Inspect(scene.Readiness,1,1).AssertFiniteRange();
        ImageAssertions.Inspect(scene.Geometry,2,2).AssertFiniteRange();
        byte[] light=GpuLocalRead.Rgba8(scene.Light,0,0);
        for(int c=0;c<4;c++)Assert.Equal((byte)(0x44332211u>>(8*c)),light[c]);
        ImageAssertions.Inspect(scene.Legacy,23,23).AssertFiniteRange();
        version++;
        partition.Prepare(plan);
        ImageAssertions.Inspect(scene.Readiness,0,0).AssertFiniteRange();
        coordinator.Pump(11); partition.Service(plan);
        var moved = TraceGeometryCoverage.Plan(new(anchor + 1000, 40, anchor + 1000), true, null, 256);
        partition.Prepare(moved);
        ImageAssertions.Inspect(scene.Readiness,0,0).AssertFiniteRange();
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Native 2D material uploads preserve normalized bytes and caller pixel-store state.</summary>
    [Fact]
    public void MaterialByteUploadPreservesAlignment()
    {
        EnsureContextValid();
        using var texture = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 3), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 8);
        try
        {
            byte[] expected = Enumerable.Range(0, 12).Select(i => (byte)(i * 19)).ToArray();
            texture.UploadDataImmediate(expected);
            GL.GetInteger(GetPName.UnpackAlignment, out int alignment); Assert.Equal(8, alignment);
            ImageAssertions.CompareRgba8(texture,expected).AssertEqual();
        }
        finally { GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4); }
    }

    #endregion
}
