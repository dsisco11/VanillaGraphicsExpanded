using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

using VanillaGraphicsExpanded.Voxels.ChunkProcessing;

namespace VanillaGraphicsExpanded.Tests.Unit;

public sealed class ChunkProcessingServicePhase3Tests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task Snapshot_Shared_Across_Processors_And_Disposed_Once_After_All_Leases_Complete()
    {
        CancellationToken testCt = TestContext.Current.CancellationToken;

        var key = ChunkKey.FromChunkCoords(7, 8, 9);

        var versionProvider = new TestChunkVersionProvider();
        versionProvider.SetCurrentVersion(key, 1);

        var snapshotSource = new CountingSnapshotSource();

        using var proc1Gate = new ManualResetEventSlim(false);
        using var proc2Gate = new ManualResetEventSlim(false);

        var processor1 = new BlockingProcessor("ProcA", proc1Gate);
        var processor2 = new BlockingProcessor("ProcB", proc2Gate);

        using var service = new ChunkProcessingService(
            snapshotSource: snapshotSource,
            versionProvider: versionProvider,
            options: new ChunkProcessingServiceOptions
            {
                WorkerCount = 2,
                ShutdownTimeout = TimeSpan.FromSeconds(1),
            });

        Task<ChunkWorkResult<string>> t1 = service.RequestAsync(key, 1, processor1, ct: testCt);
        Task<ChunkWorkResult<string>> t2 = service.RequestAsync(key, 1, processor2, ct: testCt);

        await snapshotSource.SnapshotCreated.Task.WaitAsync(TimeSpan.FromSeconds(1), testCt);
        await processor1.Started.Task.WaitAsync(TimeSpan.FromSeconds(1), testCt);
        await processor2.Started.Task.WaitAsync(TimeSpan.FromSeconds(1), testCt);

        Assert.Equal(1, snapshotSource.CreateCount);
        Assert.NotNull(snapshotSource.LastSnapshot);
        Assert.Equal(0, snapshotSource.LastSnapshot!.DisposeCount);

        proc1Gate.Set();
        proc2Gate.Set();

        ChunkWorkResult<string> r1 = await t1.WaitAsync(TimeSpan.FromSeconds(2), testCt);
        ChunkWorkResult<string> r2 = await t2.WaitAsync(TimeSpan.FromSeconds(2), testCt);

        Assert.Equal(ChunkWorkStatus.Success, r1.Status);
        Assert.Equal(ChunkWorkStatus.Success, r2.Status);

        Assert.Equal(1, snapshotSource.LastSnapshot!.DisposeCount);
    }

    private sealed class TestChunkVersionProvider : IChunkVersionProvider
    {
        private readonly ConcurrentDictionary<ChunkKey, int> currentVersion = new();

        public int GetCurrentVersion(ChunkKey key) => currentVersion.GetValueOrDefault(key);

        public void SetCurrentVersion(ChunkKey key, int version) => currentVersion[key] = version;
    }

    private sealed class CountingSnapshotSource : IChunkSnapshotSource
    {
        private int createCount;

        public int CreateCount => Volatile.Read(ref createCount);

        public TaskCompletionSource SnapshotCreated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CountingSnapshot? LastSnapshot { get; private set; }

        public ValueTask<IChunkSnapshot?> TryCreateSnapshotAsync(ChunkKey key, int expectedVersion, CancellationToken ct)
        {
            Interlocked.Increment(ref createCount);

            var snap = new CountingSnapshot(key, expectedVersion);
            LastSnapshot = snap;
            SnapshotCreated.TrySetResult();

            return ValueTask.FromResult<IChunkSnapshot?>(snap);
        }

        internal sealed class CountingSnapshot(ChunkKey key, int version) : IChunkSnapshot
        {
            private int disposed;

            public int DisposeCount => Volatile.Read(ref disposed);

            public ChunkKey Key => key;

            public int Version => version;

            public int SizeX => 0;

            public int SizeY => 0;

            public int SizeZ => 0;

            public void Dispose()
            {
                if (Interlocked.Increment(ref disposed) != 1)
                {
                    throw new InvalidOperationException("Snapshot disposed more than once");
                }
            }
        }
    }

    private sealed class BlockingProcessor : IChunkProcessor<string>
    {
        private readonly ManualResetEventSlim gate;

        public BlockingProcessor(string id, ManualResetEventSlim gate)
        {
            Id = id;
            this.gate = gate;
        }

        public string Id { get; }

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<string> ProcessAsync(IChunkSnapshot snapshot, CancellationToken ct)
        {
            Started.TrySetResult();
            gate.Wait(ct);
            return ValueTask.FromResult("artifact");
        }
    }
}
