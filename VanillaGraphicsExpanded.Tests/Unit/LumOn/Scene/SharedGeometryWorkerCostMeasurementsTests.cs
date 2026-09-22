using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Tests.Fixtures.NearField;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.Scene;

/// <summary>Records real bulk capture and packing costs separately from frame-thread publication measurements.</summary>
public sealed class SharedGeometryWorkerCostMeasurementsTests
{
    private readonly ITestOutputHelper output;
    /// <summary>Publishes synthetic worker measurements into the test receipt.</summary>
    public SharedGeometryWorkerCostMeasurementsTests(ITestOutputHelper output) => this.output = output;

    /// <summary>Captures eight controlled real engine palettes through two production chunk workers.</summary>
    [Fact]
    public async Task BulkCaptureRunsOnWorkers()
    {
        var fixture = new NearFieldCaptureFixture();
        var measured = new MeasuredSource(new TraceGeometrySnapshotSource(fixture.Accessor, id => fixture.Blocks[id],
            fixture.Versions, new(), fixture.Lighting));
        using var service = new ChunkProcessingService(measured, fixture.Versions, new() { WorkerCount = 2, ArtifactCacheBudgetBytes = 0 });
        var results = await Task.Factory.StartNew(() =>
        {
            int caller = Environment.CurrentManagedThreadId;
            var requests = Enumerable.Range(0,8).Select(i=>service.RequestAsync(ChunkKey.FromChunkCoords(i,4,0),0,
                new TraceGeometryChunkProcessor(),ct:TestContext.Current.CancellationToken)).ToArray();
            var completed = Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(30),TestContext.Current.CancellationToken).GetAwaiter().GetResult();
            Assert.All(fixture.CaptureThreads,id=>Assert.NotEqual(caller,id)); return completed;
        },TestContext.Current.CancellationToken,TaskCreationOptions.LongRunning,TaskScheduler.Default);
        Assert.All(results,result=>Assert.Equal(ChunkWorkStatus.Success,result.Status));
        Assert.Equal(8,measured.Rows.Count); Assert.Equal(16,fixture.ChunkLookups);
        output.WriteLine("WORKER_COST "+JsonSerializer.Serialize(new {
            captures=measured.Rows.Count,chunkIdentityLookups=fixture.ChunkLookups,
            captureMs=measured.Rows.Sum(row=>row.Milliseconds),peakCaptureMs=measured.Rows.Max(row=>row.Milliseconds),
            workerAllocatedBytes=measured.Rows.Sum(row=>row.Allocated),
            snapshotPayloadBytes=measured.Rows.Sum(row=>row.Payload),retainedResultPayloadBytes=results.Sum(result=>result.Artifact!.EstimatedBytes),
            scope="actual engine palette bulk reads on two workers; synthetic dark-air data; allocation includes pool warmup; no live-game timing claim" }));
    }

    /// <summary>Observes production source work without changing capture or snapshot lifetime semantics.</summary>
    private sealed class MeasuredSource(IChunkSnapshotSource source) : IChunkSnapshotSource
    {
        public ConcurrentBag<(double Milliseconds,long Allocated,long Payload)> Rows { get; } = new();
        /// <summary>Times the synchronous worker capture and records its actual returned packed payload length.</summary>
        public ValueTask<IChunkSnapshot?> TryCreateSnapshotAsync(ChunkKey key,int version,CancellationToken cancellation)
        {
            long allocated=GC.GetAllocatedBytesForCurrentThread(),start=Stopwatch.GetTimestamp();
            var snapshot=source.TryCreateSnapshotAsync(key,version,cancellation).GetAwaiter().GetResult();
            double milliseconds=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            long bytes=GC.GetAllocatedBytesForCurrentThread()-allocated;
            Assert.IsType<PooledChunkSnapshot<TraceGeometryVoxel>>(snapshot);
            Rows.Add((milliseconds,bytes,((PooledChunkSnapshot<TraceGeometryVoxel>)snapshot!).Voxels.Length*12L));
            return ValueTask.FromResult<IChunkSnapshot?>(snapshot);
        }
    }
}
