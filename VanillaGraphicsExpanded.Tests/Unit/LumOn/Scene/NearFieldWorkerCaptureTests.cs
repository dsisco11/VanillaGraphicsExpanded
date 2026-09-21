using System.Numerics;
using VanillaGraphicsExpanded.LumOn.Scene.NearField;
using VanillaGraphicsExpanded.Tests.Fixtures.NearField;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.Scene;

/// <summary>Exercises production worker capture with actual engine block and lighting palettes.</summary>
public sealed class NearFieldWorkerCaptureTests
{
    private readonly ITestOutputHelper output;

    /// <summary>Records controlled worker timing separately from live runtime frame cost.</summary>
    public NearFieldWorkerCaptureTests(ITestOutputHelper output) => this.output = output;

    #region Worker and lifetime behavior
    /// <summary>The real chunk service performs world capture off the calling thread and preserves voxel lighting.</summary>
    [Fact]
    public async Task ChunkServiceCapturesOnWorkerWithoutPerVoxelWorldQueries()
    {
        var fixture = new NearFieldCaptureFixture();
        uint light = 17u | (23u << 5) | (11u << 10) | (5u << 16);
        fixture.Data.SetLight((3 * 32 + 5) * 32 + 7, light);
        var source = fixture.CreateSource();
        using var service = new ChunkProcessingService(source, fixture.Versions, new() { WorkerCount = 2 });
        int caller = Environment.CurrentManagedThreadId;
        var result = await service.RequestAsync(default, 0, new NearFieldChunkProcessor(), ct: TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(ChunkWorkStatus.Success, result.Status);
        Assert.All(fixture.CaptureThreads, thread => Assert.NotEqual(caller, thread));
        Assert.Single(fixture.CaptureThreads);
        Assert.Equal(2, fixture.ChunkLookups);
        var cells = result.Artifact!.Extract(new(0, 0, 0)).Cells;
        Assert.Equal(1u, cells[(3 * 16 + 5) * 16 + 7].Geometry);
        Assert.Equal(fixture.Lighting.Decode(light), cells[(3 * 16 + 5) * 16 + 7].Light);
        Assert.Equal(Vector4.Zero, cells[0].Light);
        Assert.Equal(1, source.CaptureCount);
        output.WriteLine($"SYNTHETIC captures={source.CaptureCount} workerTotalMs={source.CaptureMilliseconds:F3} " +
            $"workerPeakMs={source.PeakCaptureMilliseconds:F3} chunkLookups={fixture.ChunkLookups}");
    }

    /// <summary>Edits and unloading during evaluation must prevent publication of the captured revision.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MidCaptureInvalidationDiscardsSnapshot(bool unload)
    {
        var fixture = new NearFieldCaptureFixture();
        fixture.OnBlock = () => { if (unload) fixture.Loaded = false; else fixture.Versions.MarkDirty(default); };
        Assert.Null(await fixture.CreateSource().TryCreateSnapshotAsync(default, 0, CancellationToken.None));
    }

    /// <summary>Cancellation cannot retain palette read locks or poison subsequent captures.</summary>
    [Fact]
    public async Task CancelledCaptureAllowsFollowingCapture()
    {
        var fixture = new NearFieldCaptureFixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Data.SetLight(0, 1);
        fixture.OnUnpack = cancellation.Cancel;
        var source = fixture.CreateSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await source.TryCreateSnapshotAsync(default, 0, cancellation.Token));
        fixture.OnUnpack = null;
        using var snapshot = await source.TryCreateSnapshotAsync(default, 0, CancellationToken.None);
        Assert.NotNull(snapshot);
    }
    #endregion

    #region Engine data equivalence
    /// <summary>Solid fluid-layer geometry takes precedence; nonsolid fluid leaves the solid layer authoritative.</summary>
    [Fact]
    public async Task CapturedGeometryPreservesMostSolidLayerSelection()
    {
        var fixture = new NearFieldCaptureFixture();
        fixture.Blocks[1] = new Block { BlockId = 1, CollisionBoxes = [new Cuboidf(0, 0, 0, 1, .5f, 1)] };
        fixture.Blocks[2] = new Block { BlockId = 2, CollisionBoxes = Block.DefaultCollisionSelectionBoxes };
        fixture.Blocks[3] = new Block { BlockId = 3, SideSolid = default };
        fixture.Data[0] = 1; fixture.Data.SetFluid(0, 2);
        fixture.Data[1] = 1; fixture.Data.SetFluid(1, 3);
        using var snapshot = await fixture.CreateSource().TryCreateSnapshotAsync(default, 0, TestContext.Current.CancellationToken);
        var cells = Assert.IsType<PooledChunkSnapshot<NearFieldSourceCell>>(snapshot).Voxels;
        Assert.Equal(2u, cells.Span[0].Geometry & 3u);
        Assert.Equal(0u, cells.Span[1].Geometry & 3u);
    }

    /// <summary>Bulk copy preserves independently varying solid, fluid and packed-light palettes.</summary>
    [Fact]
    public void BulkReaderCopiesEveryVoxelAndBothBlockLayers()
    {
        var fixture = new NearFieldCaptureFixture();
        for (int i = 0; i < 32768; i++)
        {
            fixture.Data[i] = i % 7;
            fixture.Data.SetFluid(i, i % 3);
            fixture.Data.SetLight(i, (uint)((i & 31) | ((i & 31) << 5) | ((i & 63) << 10) | ((i & 7) << 16)));
        }
        var solids = new int[32768]; var fluids = new int[32768]; var lights = new uint[32768];
        NearFieldChunkBulkReader.Copy(fixture.Chunk, solids, fluids, lights, CancellationToken.None);
        for (int i = 0; i < 32768; i++)
        {
            Assert.Equal(i % 7, solids[i]);
            Assert.Equal(i % 3, fluids[i]);
            Assert.Equal((uint)((i & 31) | ((i & 31) << 5) | ((i & 63) << 10) | ((i & 7) << 16)), lights[i]);
        }
    }

    /// <summary>All packed light values use the accessor's HSV conversion and channel ordering.</summary>
    [Fact]
    public void PackedLightingMatchesEngineConversion()
    {
        var fixture = new NearFieldCaptureFixture();
        for (uint packed = 0; packed < 1 << 19; packed++)
        {
            int rgb = ColorUtil.HsvToRgb((int)((packed >> 10) & 63) * 4,
                (int)((packed >> 16) & 7) * 36, (int)(((packed >> 5) & 31) / 31f * 255));
            var expected = new Vector4((rgb >> 16) / 255f, ((rgb >> 8) & 255) / 255f,
                (rgb & 255) / 255f, (packed & 31) / 31f);
            Assert.Equal(expected, fixture.Lighting.Decode(packed));
        }
    }
    #endregion
}
