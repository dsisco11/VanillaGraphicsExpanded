using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Tests.Support.PBR.Materials;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.WorldPartition;
using VanillaGraphicsExpanded.Tests.Support.Numerics;

namespace VanillaGraphicsExpanded.Tests.Integration.LumOn;

/// <summary>Exercises native GPU companion uploads and ring retirement through coordinator authorization.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class TraceGeometryPublicationTests : RenderTestBase
{
    #region Public API
    /// <summary>Uses the existing headless GL owner.</summary>
    public TraceGeometryPublicationTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Publication scenarios
    /// <summary>Resolved face IDs and independent readiness flags survive budgeted table uploads before cells become ready.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MaterialTablesPublishCoherentlyUnderSmallBudget(bool hitReady)
    {
        // Arrange: Configure a real partition and native scene with a single resolved material and small upload budget.
        EnsureContextValid();
        using var fixture = new ScopedPbrMaterialFixture();
        fixture.SetReadiness(true, hitReady);
        var materials = new TraceGeometryMaterials();
        uint id = materials.Resolve(fixture.Cube);
        var expected = materials.Snapshot();
        var coordinator = new PartitionCoordinator(new(1024, 256, 128, 128, 65536));
        TraceGeometryGpuScene? sceneOwner = new TraceGeometryGpuScene(48);
        TraceGeometrySourceCache? cacheOwner = null;
        try
        {
            var scene = sceneOwner!;
            cacheOwner = new TraceGeometrySourceCache((key, version, _) => Task.FromResult<TraceGeometryChunk?>(
                new(key, version, Enumerable.Repeat(new TraceGeometryVoxel(2 | id << 2, id << 18, 0), 32768).ToArray())), _ => 0, _ => true);
            using var partition = new TraceGeometryPartition(coordinator, cacheOwner!, materials, sceneOwner!);
            // Successful construction transfers scene and cache disposal to the partition.
            sceneOwner = null;
            cacheOwner = null;
            var plan = TraceGeometryCoverage.Plan(new(0, 40, 0), true, null, 256);
            // Act: Pump source publication in bounded frames until the companion material tables converge.
            for (int i = 1; i <= 100; i++)
            {
                partition.Prepare(plan); coordinator.Pump(i);
                long bytes = scene.UploadedBytes;
                partition.Service(plan);
                Assert.InRange(scene.UploadedBytes - bytes, 0, 65536);
                if (scene.TablesRevision != expected.Revision)
                    ImageAssertions.Statistics.Inspect(scene.Readiness,0,0).AssertFiniteRange();
            }
            // Assert: Native geometry, face readiness, material bytes, and surfaces become coherent after publication.
            ImageAssertions.Statistics.Inspect(scene.Readiness,1,1).AssertFiniteRange();
            uint[] faces=expected.Faces.ToArray();
            ImageAssertions.Comparisons.CompareBits(scene.Faces,faces).AssertEqual();
            Assert.NotEqual(0u, faces[id * 4]); Assert.Equal(hitReady ? 3u : 1u, faces[id * 4 + 3] & 3u);
            Assert.Equal((uint)fixture.Cube.Id,faces[id * 4 + 3] >> 2);
            byte[] colors=expected.Colors.ToArray();
            ImageAssertions.Comparisons.CompareRgba8(scene.Materials,colors).AssertEqual();
            if(hitReady)Assert.Equal((byte)MathF.Round(GgxHemisphereReference.CapturedDiffuse(1)*255),colors[id*48]);
            ImageAssertions.Comparisons.CompareBits(scene.Surfaces,expected.Surfaces.ToArray()).AssertEqual();
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            cacheOwner?.Dispose();
            sceneOwner?.Dispose();
        }
    }

    /// <summary>Ready slots contain coherent geometry, packed/normalized light and initialized material flags.</summary>
    [Theory]
    [InlineData(0)] [InlineData(-16777216)] [InlineData(16777216)]
    public void SharedPublicationAndTeleportPreserveReadiness(int anchor)
    {
        // Arrange: Build a real coordinator/cache/partition around one constant source chunk at a signed anchor.
        EnsureContextValid();
        var coordinator = new PartitionCoordinator(new(1024, 256, 128, 128, 8 * 1024 * 1024));
        TraceGeometryGpuScene? sceneOwner = new TraceGeometryGpuScene(48);
        TraceGeometrySourceCache? cacheOwner = null;
        int version = 0;
        try
        {
            var scene = sceneOwner!;
            cacheOwner = new TraceGeometrySourceCache((key, revision, _) => Task.FromResult<TraceGeometryChunk?>(
                new(key, revision, Enumerable.Repeat(new TraceGeometryVoxel(2, 23, 0x44332211), 32768).ToArray())), _ => version, _ => true);
            using var partition = new TraceGeometryPartition(coordinator, cacheOwner!, new(), sceneOwner!);
            // Successful construction transfers scene and cache disposal to the partition.
            sceneOwner = null;
            cacheOwner = null;
            var plan = TraceGeometryCoverage.Plan(new(anchor + 1, 40, anchor + 1), true, null, 256);
            // Act: Publish companion tables and establish readiness at the initial signed anchor.
            for (int frame = 1; frame <= 10; frame++) { partition.Prepare(plan); coordinator.Pump(frame); partition.Service(plan); }
            // Assert: Native readiness and normalized light are coherent before source revision changes.
            ImageAssertions.Statistics.Inspect(scene.Readiness,1,1).AssertFiniteRange();
            ImageAssertions.Statistics.Inspect(scene.Geometry,2,2).AssertFiniteRange();
            byte[] light=GpuLocalRead.Rgba8(scene.Light,0,0);
            for(int c=0;c<4;c++)Assert.Equal((byte)(0x44332211u>>(8*c)),light[c]);
            ImageAssertions.Statistics.Inspect(scene.Legacy,23,23).AssertFiniteRange();
            // Act: Advance the source revision and verify readiness is withdrawn before bounded re-publication.
            version++;
            partition.Prepare(plan);
            // Assert: The revised slot is invalidated before the next coordinator pump.
            ImageAssertions.Statistics.Inspect(scene.Readiness,0,0).AssertFiniteRange();
            // Act: Complete the new revision, then move the partition to a disjoint physical window.
            coordinator.Pump(11); partition.Service(plan);
            var moved = TraceGeometryCoverage.Plan(new(anchor + 1000, 40, anchor + 1000), true, null, 256);
            partition.Prepare(moved);
            // Assert: Teleport retirement preserves valid-state encoding and native calls remain error-free.
            ImageAssertions.Statistics.Inspect(scene.Readiness,0,0).AssertFiniteRange();
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            cacheOwner?.Dispose();
            sceneOwner?.Dispose();
        }
    }

    /// <summary>Native 2D material uploads preserve normalized bytes and caller pixel-store state.</summary>
    [Fact]
    public void MaterialByteUploadPreservesAlignment()
    {
        // Arrange: Create a three-pixel native RGBA8 texture and set a non-default unpack alignment.
        EnsureContextValid();
        using var texture = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 3), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        GL.GetInteger(GetPName.UnpackAlignment, out int previousAlignment);
        try
        {
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 8);
            // Act: Upload caller-authored bytes through the production texture path.
            byte[] expected = Enumerable.Range(0, 12).Select(i => (byte)(i * 19)).ToArray();
            texture.UploadDataImmediate(expected);
            // Assert: The native unpack setting is restored and all normalized bytes arrive unchanged.
            GL.GetInteger(GetPName.UnpackAlignment, out int alignment); Assert.Equal(8, alignment);
            ImageAssertions.Comparisons.CompareRgba8(texture,expected).AssertEqual();
        }
        finally { GL.PixelStore(PixelStoreParameter.UnpackAlignment, previousAlignment); }
    }

    #endregion

    #endregion

    #region Protected
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("inspection", 2, 0);
        programs.Prepare("compare", 2, 0);
        programs.Prepare("compare", 0, 0);
        programs.Prepare("inspection", 0, 0);
        programs.Prepare("projection-observation", 0, 0);
    }
    #endregion
}
