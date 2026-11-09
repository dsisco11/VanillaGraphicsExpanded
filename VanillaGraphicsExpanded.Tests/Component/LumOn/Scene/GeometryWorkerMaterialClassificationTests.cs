using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VanillaGraphicsExpanded.Tests.Support.Lighting;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn.Scene;

/// <summary>Verifies worker geometry classification remains available before material data is ready.</summary>
public sealed class GeometryWorkerMaterialClassificationTests
{
    #region Public API
    /// <summary>Palette capture preserves unsupported shapes and opaque hits independently of material readiness.</summary>
    [Fact]
    public async Task ClassificationDoesNotDependOnMaterialReadiness()
    {
        // Arrange: capture full and partial collision shapes while material readiness remains absent.
        var fixture = new NearFieldCaptureFixture();
        fixture.Blocks[1] = new Block { BlockId = 1, CollisionBoxes = Block.DefaultCollisionSelectionBoxes };
        fixture.Blocks[16385] = new Block { BlockId = 16385, CollisionBoxes = [new Cuboidf(0, 0, 0, 1, .5f, 1)] };
        fixture.Data[0] = 1;
        fixture.Data[1] = 16385;
        var materials = new TraceGeometryMaterials();
        var source = new TraceGeometrySnapshotSource(fixture.Accessor, id => fixture.Blocks[id], fixture.Versions, materials, fixture.Lighting);
        using var service = new ChunkProcessingService(source, fixture.Versions, new() { WorkerCount = 2 });

        // Act: request the production geometry artifact from the real worker service.
        var result = await service.RequestAsync(default, 0, new TraceGeometryChunkProcessor(), ct: TestContext.Current.CancellationToken);

        // Assert: collision classes and block identities survive even though no material face records were published.
        Assert.Equal(ChunkWorkStatus.Success, result.Status);
        var cell = result.Artifact!.Extract(default);
        Assert.Equal(2u, cell.Geometry[0] & 3);
        Assert.Equal(3u, cell.Geometry[1] & 3);
        uint full = cell.Geometry[0] >> 2;
        uint partial = cell.Geometry[1] >> 2;
        Assert.NotEqual(0u, full);
        Assert.NotEqual(full, partial);
        Assert.Equal(full, cell.Legacy[0] >> 18);
        Assert.Equal(partial, cell.Legacy[1] >> 18);
        var tables = materials.Snapshot();
        Assert.Equal(0u, tables.Faces[(int)full * 4 + 3] & 3u);
        Assert.Equal(0u, tables.Faces[(int)partial * 4 + 3] & 3u);
        Assert.Equal(1u, tables.Faces[(int)full * 4 + 3] >> 2);
        Assert.Equal(16385u, tables.Faces[(int)partial * 4 + 3] >> 2);
        Assert.True(cell.Unsupported);
    }
    #endregion
}










