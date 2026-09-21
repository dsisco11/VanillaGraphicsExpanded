using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.Scene;

/// <summary>Exercises source pressure and retry behavior independently of GPU publication timing.</summary>
public sealed class TraceGeometrySourceCacheTests
{
    #region Retry and cancellation
    /// <summary>An explicit load/edit notification bypasses the missing-source retry delay.</summary>
    [Fact]
    public void MissingSourceStaysUnknownUntilLoaded()
    {
        bool loaded = false;
        int reads = 0;
        using var cache = new TraceGeometrySourceCache((key, version, _) =>
        {
            reads++;
            return Task.FromResult<TraceGeometryChunk?>(new(key, version, new TraceGeometryVoxel[32768]));
        }, _ => 0, _ => loaded);
        var plan = TraceGeometryCoverage.Plan(new(1, 40, 1), true, null, 256);
        PartitionCoordinate cell = new(0, 2, 0);
        cache.Update([cell], plan);
        Assert.False(cache.HasSnapshot(cell)); Assert.Equal(0, reads);
        loaded = true;
        cache.Update([cell], plan); Assert.Equal(0, reads);
        cache.Dirty(ChunkKey.FromChunkCoords(0, 1, 0));
        cache.Update([cell], plan);
        Assert.True(cache.HasSnapshot(cell)); Assert.Equal(1, reads);
        loaded = false;
        Assert.False(cache.HasSnapshot(cell));
    }

    /// <summary>Repeated teleports cannot bypass the eight-worker bound with unacknowledged cancellations.</summary>
    [Fact]
    public void CancelledSourcesKeepCreditUntilAcknowledged()
    {
        var tasks = new List<TaskCompletionSource<TraceGeometryChunk?>>();
        using var cache = new TraceGeometrySourceCache((_, _, _) =>
        {
            var task = new TaskCompletionSource<TraceGeometryChunk?>(); tasks.Add(task); return task.Task;
        }, _ => 0, _ => true);
        for (int i = 0; i < 10; i++)
        {
            var plan = TraceGeometryCoverage.Plan(new(i * 1000, 40, 0), true, null, 256);
            var cells = new PartitionLayout(new(16, 16, 16)).Intersecting(plan.NearField!.Value).ToArray();
            cache.Update(cells, plan);
            Assert.InRange(cache.InFlight, 0, 8);
        }
        Assert.Equal(8, tasks.Count);
        foreach (var task in tasks) task.SetResult(null);
        var destination = TraceGeometryCoverage.Plan(new(20000, 40, 0), true, null, 256);
        cache.Update(new PartitionLayout(new(16, 16, 16)).Intersecting(destination.NearField!.Value).ToArray(), destination);
        Assert.Equal(10, tasks.Count); Assert.Equal(2, cache.InFlight);
    }
    #endregion
}
