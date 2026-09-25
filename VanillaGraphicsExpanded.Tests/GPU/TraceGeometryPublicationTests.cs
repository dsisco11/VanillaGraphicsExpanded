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
[Trait("Category", "GPU")]
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
                Assert.All(Read(scene.Readiness, PixelFormat.RedInteger, 27), value => Assert.Equal((byte)0, value));
        }
        Assert.All(Read(scene.Readiness, PixelFormat.RedInteger, 27), value => Assert.Equal((byte)1, value));
        uint[] faces = new uint[16384 * 4];
        using (GlStateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, scene.Faces.TextureId))
            GL.GetTexImage(TextureTarget.Texture2D, 0, PixelFormat.RgbaInteger, PixelType.UnsignedInt, faces);
        Assert.Equal(expected.Faces.ToArray(), faces);
        Assert.NotEqual(0u, faces[id * 4]); Assert.Equal(hitReady ? 3u : 1u, faces[id * 4 + 3] & 3u);
        Assert.Equal((uint)fixture.Cube.Id,faces[id * 4 + 3] >> 2);
        byte[] colors = new byte[16384 * 48];
        using (GlStateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, scene.Materials.TextureId))
            GL.GetTexImage(TextureTarget.Texture2D, 0, PixelFormat.Rgba, PixelType.UnsignedByte, colors);
        Assert.Equal(expected.Colors.ToArray(), colors);
        if (hitReady) Assert.Equal(255, colors[id * 48]);
        uint[] surfaces = new uint[65536 * 4];
        using (GlStateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, scene.Surfaces.TextureId))
            GL.GetTexImage(TextureTarget.Texture2D, 0, PixelFormat.RgbaInteger, PixelType.UnsignedInt, surfaces);
        Assert.Equal(expected.Surfaces.ToArray(), surfaces);
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
        Assert.All(Read(scene.Readiness, PixelFormat.RedInteger, 27), value => Assert.Equal((byte)1, value));
        uint[] geometry = new uint[48 * 48 * 48];
        using (GlStateCache.Current.BindTextureScope(TextureTarget.Texture3D, 0, scene.Geometry.TextureId))
            GL.GetTexImage(TextureTarget.Texture3D, 0, PixelFormat.RedInteger, PixelType.UnsignedInt, geometry);
        Assert.All(geometry, value => Assert.Equal(2u, value));
        byte[] light = Read(scene.Light, PixelFormat.Rgba, geometry.Length * 4);
        for (int c = 0; c < 4; c++) Assert.Equal((byte)(0x44332211u >> (8 * c)), light[c]);
        uint[] legacy = new uint[geometry.Length];
        using (GlStateCache.Current.BindTextureScope(TextureTarget.Texture3D, 0, scene.Legacy.TextureId))
            GL.GetTexImage(TextureTarget.Texture3D, 0, PixelFormat.RedInteger, PixelType.UnsignedInt, legacy);
        Assert.All(legacy, value => Assert.Equal(23u, value));

        version++;
        partition.Prepare(plan);
        Assert.All(Read(scene.Readiness, PixelFormat.RedInteger, 27), value => Assert.Equal((byte)0, value));
        coordinator.Pump(11); partition.Service(plan);
        var moved = TraceGeometryCoverage.Plan(new(anchor + 1000, 40, anchor + 1000), true, null, 256);
        partition.Prepare(moved);
        Assert.All(Read(scene.Readiness, PixelFormat.RedInteger, 27), value => Assert.Equal((byte)0, value));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Native 2D material uploads preserve normalized bytes and caller pixel-store state.</summary>
    [Fact]
    public void MaterialByteUploadPreservesAlignment()
    {
        EnsureContextValid();
        using var texture = Texture2D.Create(3, 1, PixelInternalFormat.Rgba8);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 8);
        try
        {
            byte[] expected = Enumerable.Range(0, 12).Select(i => (byte)(i * 19)).ToArray();
            texture.UploadDataImmediate(expected);
            GL.GetInteger(GetPName.UnpackAlignment, out int alignment); Assert.Equal(8, alignment);
            byte[] observed = new byte[12];
            using (GlStateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, texture.TextureId))
                GL.GetTexImage(TextureTarget.Texture2D, 0, PixelFormat.Rgba, PixelType.UnsignedByte, observed);
            Assert.Equal(expected, observed);
        }
        finally { GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4); }
    }

    /// <summary>Reads tightly packed 3D bytes without relying on the default four-byte row alignment.</summary>
    private static byte[] Read(Texture3D texture, PixelFormat format, int count)
    {
        byte[] data = new byte[count];
        GL.GetInteger(GetPName.PackAlignment, out int alignment);
        GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
        try
        {
            using var binding = GlStateCache.Current.BindTextureScope(TextureTarget.Texture3D, 0, texture.TextureId);
            GL.GetTexImage(TextureTarget.Texture3D, 0, format, PixelType.UnsignedByte, data);
        }
        finally { GL.PixelStore(PixelStoreParameter.PackAlignment, alignment); }
        return data;
    }
    #endregion
}
