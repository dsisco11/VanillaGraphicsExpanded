using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Tests.Fixtures.NearField;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using VanillaGraphicsExpanded.WorldPartition;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.Scene;

/// <summary>Shared backend coverage, coalescing and stale-publication tests independent of consumer shaders.</summary>
public sealed class TraceGeometryBackendTests
{
    #region Shared coverage and publication
    /// <summary>Every camera offset retains both logical windows inside the cell-aligned allocation.</summary>
    [Theory]
    [InlineData(16)] [InlineData(32)] [InlineData(64)] [InlineData(128)]
    public void CoveragePreservesDomainsAtEveryOffset(int n)
    {
        foreach (int anchor in new[] { -16777216, -32, 0, 16777216 })
        for (int x = 0; x < 16; x++)
        for (int y = 0; y < 16; y++)
        for (int z = 0; z < 16; z++)
        {
            var p = TraceGeometryCoverage.Plan(new(anchor + x + .5, 40 + y, anchor + z), true, n, 256);
            foreach (var bounds in new[] { p.NearField!.Value, p.Surface!.Value })
            {
                Assert.True(bounds.Min.X >= p.Window.Min.X && bounds.Max.X <= p.Window.Max.X);
                Assert.True(bounds.Min.Y >= p.Window.Min.Y && bounds.Max.Y <= p.Window.Max.Y);
                Assert.True(bounds.Min.Z >= p.Window.Min.Z && bounds.Max.Z <= p.Window.Max.Z);
            }
            Assert.Equal(48, p.NearField.Value.Max.X - p.NearField.Value.Min.X);
        }
    }

    /// <summary>Overlapping near and surface demand causes one capture per source revision and one publication per cell.</summary>
    [Fact]
    public void OverlappingConsumersShareCaptureAndPublication()
    {
        using var f = new TraceGeometryFixture();
        var plan = TraceGeometryCoverage.Plan(new(1, 40, 1), true, 32, 256);
        for (int frame = 0; frame < 20; frame++) { f.Frame(plan); f.Complete(); }
        Assert.Equal(27, f.Backend.Ready.Count);
        Assert.Equal(27, f.Backend.Publications.Count);
        Assert.Equal(f.Requests.Count, f.Requests.Select(r => (r.Key, r.Version)).Distinct().Count());
        Assert.True(f.Cache.SnapshotBytes <= 8 * 393216);
        Assert.True(f.Cache.InFlight <= 8);
    }

    /// <summary>Only the entering slab is republished after a cell-aligned movement.</summary>
    [Fact]
    public void MovementPreservesEighteenOverlappingCells()
    {
        using var f = new TraceGeometryFixture();
        var old = TraceGeometryCoverage.Plan(new(1, 40, 1), true, null, 256);
        for (int i = 0; i < 20; i++) { f.Frame(old); f.Complete(); }
        var keys = f.Backend.Ready.Keys.ToHashSet();
        var next = TraceGeometryCoverage.Plan(new(17, 40, 1), true, null, 256);
        f.Frame(next);
        Assert.Equal(18, f.Backend.Ready.Keys.Count(keys.Contains));
        for (int i = 0; i < 20; i++) { f.Complete(); f.Frame(next); }
        Assert.Equal(27, f.Backend.Ready.Count);
        Assert.Equal(36, f.Backend.Publications.Count);
    }

    /// <summary>Late source completions after teleport cannot publish into the destination domain.</summary>
    [Fact]
    public void TeleportRejectsLateSourcesAndRetainsCancelledCredit()
    {
        using var f = new TraceGeometryFixture();
        f.Frame(TraceGeometryCoverage.Plan(new(1, 40, 1), true, null, 256));
        var old = f.Requests.ToArray();
        var next = TraceGeometryCoverage.Plan(new(1000, 40, 1000), true, null, 256);
        f.Frame(next);
        Assert.Equal(4, f.Cache.InFlight);
        foreach (var r in old) r.Completion.SetResult(new(r.Key, r.Version, Enumerable.Repeat(new TraceGeometryVoxel(2, 0, 0), 32768).ToArray()));
        for (int i = 0; i < 20; i++) { f.Complete(); f.Frame(next); }
        Assert.Equal(27, f.Backend.Ready.Count);
        Assert.All(f.Backend.Ready.Values, cell => Assert.Equal(1u, cell.Geometry[0]));
    }

    /// <summary>Dirty dependencies hide old data before replacement publication.</summary>
    [Fact]
    public void EditInvalidatesAllDependentCells()
    {
        using var f = new TraceGeometryFixture();
        var plan = TraceGeometryCoverage.Plan(new(1, 40, 1), true, null, 256);
        for (int i = 0; i < 20; i++) { f.Frame(plan); f.Complete(); }
        var key = f.Requests[0].Key; f.Versions[key] = 1;
        f.Frame(plan);
        Assert.True(f.Backend.Ready.Count < 27);
        for (int i = 0; i < 20; i++) { f.Complete(3); f.Frame(plan); }
        Assert.Equal(27, f.Backend.Ready.Count);
        Assert.Contains(f.Backend.Ready.Values, c => c.Unsupported);
    }
    #endregion

    #region Worker data and lifetimes
    /// <summary>The production shared source uses engine bulk reads off the caller thread.</summary>
    [Fact]
    public async Task SharedWorkerCapturesBothLightEncodings()
    {
        var f = new NearFieldCaptureFixture();
        uint light = 17u | (23u << 5) | (11u << 10) | (5u << 16);
        f.Data.SetLight((3 * 32 + 5) * 32 + 7, light);
        var materials = new TraceGeometryMaterials();
        var source = new TraceGeometrySnapshotSource(f.Accessor, id => f.Blocks[id], f.Versions, materials, f.Lighting);
        using var service = new ChunkProcessingService(source, f.Versions, new() { WorkerCount = 2 });
        int caller = Environment.CurrentManagedThreadId;
        var result = await service.RequestAsync(default, 0, new TraceGeometryChunkProcessor(), ct: TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(ChunkWorkStatus.Success, result.Status);
        Assert.All(f.CaptureThreads, t => Assert.NotEqual(caller, t));
        Assert.Equal(2, f.ChunkLookups);
        var cell = result.Artifact!.Extract(new(0, 0, 0));
        int index = (5 * 16 + 3) * 16 + 7;
        Assert.Equal(1u, cell.Geometry[index]);
        Assert.Equal(23u, cell.Legacy[index] & 63);
        Assert.Equal(17u, (cell.Legacy[index] >> 6) & 63);
        uint expected = TraceGeometryVoxel.PackLight(f.Lighting.Decode(light));
        for (int c = 0; c < 4; c++) Assert.Equal((byte)(expected >> (8 * c)), cell.Light[index * 4 + c]);
        // The installed accessor uses HsvToRgba, whose low 24 bits feed the old high-byte-red registry unchanged.
        int legacyRgb = ColorUtil.HsvToRgba(11 * 4, 5 * 36, (int)(23 / 31f * 255), (int)(17 / 31f * 255));
        var lut = materials.Snapshot();
        int lightId = (int)((cell.Legacy[index] >> 12) & 63);
        Assert.NotEqual(0, lightId);
        Assert.Equal(((legacyRgb >> 16) & 255) / 32 / 7f, lut.Lights[lightId * 4]);
        Assert.Equal(((legacyRgb >> 8) & 255) / 32 / 7f, lut.Lights[lightId * 4 + 1]);
        Assert.Equal((legacyRgb & 255) / 32 / 7f, lut.Lights[lightId * 4 + 2]);
    }

    /// <summary>Missing and mid-capture superseded chunks must remain unavailable rather than empty.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task MissingOrChangedSourceHasNoSnapshot(bool edit)
    {
        var f = new NearFieldCaptureFixture { Loaded = edit };
        var source = new TraceGeometrySnapshotSource(f.Accessor, id =>
        { if (edit) f.Versions.MarkDirty(default); return f.Blocks[id]; }, f.Versions, new(), f.Lighting);
        Assert.Null(await source.TryCreateSnapshotAsync(default, 0, CancellationToken.None));
    }

    /// <summary>Real palette capture preserves unsupported shapes and opaque hits independently of material readiness.</summary>
    [Fact]
    public async Task ClassificationDoesNotDependOnMaterialReadiness()
    {
        var f = new NearFieldCaptureFixture();
        f.Blocks[1] = new Block { BlockId = 1, CollisionBoxes = Block.DefaultCollisionSelectionBoxes };
        f.Blocks[16385] = new Block { BlockId = 16385, CollisionBoxes = [new Cuboidf(0, 0, 0, 1, .5f, 1)] };
        f.Data[0] = 1; f.Data[1] = 16385;
        var materials = new TraceGeometryMaterials();
        var source = new TraceGeometrySnapshotSource(f.Accessor, id => f.Blocks[id], f.Versions, materials, f.Lighting);
        using var service = new ChunkProcessingService(source, f.Versions, new() { WorkerCount = 2 });
        var result = await service.RequestAsync(default, 0, new TraceGeometryChunkProcessor(), ct: TestContext.Current.CancellationToken);
        Assert.Equal(ChunkWorkStatus.Success, result.Status);
        var cell = result.Artifact!.Extract(default);
        Assert.Equal(2u, cell.Geometry[0] & 3);
        Assert.Equal(3u, cell.Geometry[1] & 3);
        uint a = cell.Geometry[0] >> 2, b = cell.Geometry[1] >> 2;
        Assert.NotEqual(0u, a); Assert.NotEqual(a, b);
        Assert.Equal(a, cell.Legacy[0] >> 18); Assert.Equal(b, cell.Legacy[1] >> 18);
        var tables = materials.Snapshot();
        Assert.Equal(0u, tables.Faces[(int)a * 4 + 3]);
        Assert.Equal(0u, tables.Faces[(int)b * 4 + 3]);
        Assert.True(cell.Unsupported);
    }

    /// <summary>Large surface demand cannot evict pinned subcells or repeatedly capture shared source revisions.</summary>
    [Fact]
    public void BroadCoverageMakesBoundedProgressWithoutDuplicateReads()
    {
        using var f = new TraceGeometryFixture(144);
        var plan = TraceGeometryCoverage.Plan(new(1, 120, 1), true, 128, 256);
        var layout = new PartitionLayout(new(16, 16, 16));
        int expected = layout.Intersecting(plan.Surface!.Value).Count();
        for (int i = 0; i < 160; i++)
        {
            int requests = f.Requests.Count, publications = f.Backend.Publications.Count;
            f.Frame(plan); f.Complete();
            Assert.InRange(f.Requests.Count - requests, 0, 2);
            Assert.InRange(f.Backend.Publications.Count - publications, 0, 16);
            Assert.InRange(f.Cache.InFlight, 0, 8);
            Assert.InRange(f.Cache.SnapshotBytes, 0, 8 * 393216L);
        }
        Assert.Equal(expected, f.Backend.Ready.Count);
        Assert.Equal(expected, f.Backend.Publications.Count);
        Assert.Equal(f.Requests.Count, f.Requests.Select(r => (r.Key, r.Version)).Distinct().Count());
        Assert.Contains(f.Backend.Publications.Take(16), p => !plan.IsNear(p.Coordinate));
    }

    /// <summary>Changing consumer configuration requires a fresh generation even at an unchanged allocation size.</summary>
    [Fact]
    public void ConfigurationChangeCannotReuseOldGeneration()
    {
        using var f = new TraceGeometryFixture();
        f.Frame(TraceGeometryCoverage.Plan(new(0, 40, 0), true, null, 256));
        Assert.Throws<InvalidOperationException>(() => f.Frame(TraceGeometryCoverage.Plan(new(0, 40, 0), true, 32, 256)));
    }

    /// <summary>Source workers cannot bypass a zero global capture budget.</summary>
    [Fact]
    public void GlobalCaptureBudgetPreventsSourceReads()
    {
        using var f = new TraceGeometryFixture(captureLimit: 0);
        var plan = TraceGeometryCoverage.Plan(new(0, 40, 0), true, null, 256);
        for (int i = 0; i < 10; i++) f.Frame(plan);
        Assert.Empty(f.Requests); Assert.Empty(f.Backend.Publications);
    }

    /// <summary>Dirty cancellation retains global worker credit until the old worker acknowledges completion.</summary>
    [Fact]
    public void DirtyPendingCaptureRetainsGlobalCredit()
    {
        using var f = new TraceGeometryFixture(inFlightLimit: 1);
        var plan = TraceGeometryCoverage.Plan(new(0, 40, 0), true, null, 256);
        f.Frame(plan); Assert.Single(f.Requests);
        var old = f.Requests[0]; f.Versions[old.Key] = 1;
        for (int i = 0; i < 5; i++) f.Frame(plan);
        Assert.Single(f.Requests);
        old.Completion.SetResult(null);
        for (int i = 0; i < 5; i++) f.Frame(plan);
        Assert.True(f.Requests.Count > 1);
    }

    /// <summary>Material initialization progresses under a budget that fits one cell but not an entire table.</summary>
    [Fact]
    public void TableUploadsCannotPermanentlyBlockFeasibleCells()
    {
        const long budget = TraceGeometryCell.UploadBytes + 2;
        using var f = new TraceGeometryFixture(uploadLimit: budget);
        var plan = TraceGeometryCoverage.Plan(new(0, 40, 0), true, null, 256);
        for (int i = 0; i < 120; i++)
        {
            long bytes = f.Backend.UploadedBytes;
            f.Frame(plan); f.Complete();
            Assert.InRange(f.Backend.UploadedBytes - bytes, 0, budget);
        }
        Assert.Equal(27, f.Backend.Ready.Count);
        Assert.Equal(f.Requests.Count, f.Requests.Select(r => (r.Key, r.Version)).Distinct().Count());
    }

    /// <summary>Loading clips vertically without changing either consumer's logical bounds.</summary>
    [Theory]
    [InlineData(true, false, -5)] [InlineData(true, false, 255)]
    [InlineData(false, true, -5)] [InlineData(false, true, 255)]
    public void CoverageModesClipDemandAtWorldHeight(bool near, bool surface, int y)
    {
        var plan = TraceGeometryCoverage.Plan(new(-1, y, 16777216), near, surface ? 32 : null, 256);
        var bounds = (plan.NearField ?? plan.Surface)!.Value;
        var clipped = plan.Clip(bounds);
        Assert.InRange(clipped.Min.Y, 0, 256); Assert.InRange(clipped.Max.Y, 0, 256);
        Assert.Equal(near, plan.NearField.HasValue); Assert.Equal(surface, plan.Surface.HasValue);
        Assert.Equal(bounds.Min.X, clipped.Min.X); Assert.Equal(bounds.Max.Z, clipped.Max.Z);
        Assert.True(bounds.Min.Y < 0 || bounds.Max.Y > 256);
    }
    #endregion
}
