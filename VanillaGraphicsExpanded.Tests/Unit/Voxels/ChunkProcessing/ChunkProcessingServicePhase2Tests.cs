using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

using VanillaGraphicsExpanded.Voxels.ChunkProcessing;

namespace VanillaGraphicsExpanded.Tests.Unit;

public sealed class ChunkProcessingServicePhase2Tests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task Enqueue_Newer_Version_Supersedes_Older_Queued_Immediately_Without_Snapshot()
    {
        CancellationToken testCt = TestContext.Current.CancellationToken;

        var key = ChunkKey.FromChunkCoords(1, 2, 3);
        var blockerKey = ChunkKey.FromChunkCoords(100, 100, 100);

        var versionProvider = new TestChunkVersionProvider();
        versionProvider.SetCurrentVersion(key, 2);
        versionProvider.SetCurrentVersion(blockerKey, 1);

        using var blockerGate = new ManualResetEventSlim(initialState: false);
        var blockerSnapshotStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var snapshotSource = new BlockingSnapshotSource(
            blockerKey: blockerKey,
            blockerVersion: 1,
            blockerGate: blockerGate,
            blockerSnapshotStarted: blockerSnapshotStarted);

        using var service = new ChunkProcessingService(
            snapshotSource: snapshotSource,
            versionProvider: versionProvider,
            options: new ChunkProcessingServiceOptions
            {
                WorkerCount = 1,
                ShutdownTimeout = TimeSpan.FromSeconds(1),
            });

        var blockerProcessor = new TestStringProcessor(id: "BlockerProc");
        Task<ChunkWorkResult<string>> blockerTask = service.RequestAsync(blockerKey, 1, blockerProcessor, ct: testCt);

        await blockerSnapshotStarted.Task.WaitAsync(TimeSpan.FromSeconds(1), testCt);

        var processor = new TestStringProcessor(id: "TestProc");

        Task<ChunkWorkResult<string>> v1Task = service.RequestAsync(key, 1, processor, ct: testCt);
        Task<ChunkWorkResult<string>> v2Task = service.RequestAsync(key, 2, processor, ct: testCt);

        ChunkWorkResult<string> v1Result = await v1Task.WaitAsync(TimeSpan.FromMilliseconds(250), testCt);
        Assert.Equal(ChunkWorkStatus.Superseded, v1Result.Status);

        Assert.Equal(0, snapshotSource.GetCallCount(key, 1));

        blockerGate.Set();

        ChunkWorkResult<string> v2Result = await v2Task.WaitAsync(TimeSpan.FromSeconds(2), testCt);
        Assert.Equal(ChunkWorkStatus.Success, v2Result.Status);
        Assert.Equal("artifact", v2Result.Artifact);

        Assert.Equal(1, snapshotSource.GetCallCount(key, 2));
        Assert.Equal(1, processor.CallCount);

        _ = await blockerTask.WaitAsync(TimeSpan.FromSeconds(2), testCt);
    }

    private sealed class TestChunkVersionProvider : IChunkVersionProvider
    {
        private readonly ConcurrentDictionary<ChunkKey, int> currentVersion = new();

        public int GetCurrentVersion(ChunkKey key) => currentVersion.GetValueOrDefault(key);

        public void SetCurrentVersion(ChunkKey key, int version) => currentVersion[key] = version;
    }

    private sealed class BlockingSnapshotSource : IChunkSnapshotSource
    {
        private readonly ChunkKey blockerKey;
        private readonly int blockerVersion;
        private readonly ManualResetEventSlim blockerGate;
        private readonly TaskCompletionSource blockerSnapshotStarted;

        private readonly ConcurrentDictionary<(ChunkKey Key, int Version), int> callCounts = new();

        public BlockingSnapshotSource(
            ChunkKey blockerKey,
            int blockerVersion,
            ManualResetEventSlim blockerGate,
            TaskCompletionSource blockerSnapshotStarted)
        {
            this.blockerKey = blockerKey;
            this.blockerVersion = blockerVersion;
            this.blockerGate = blockerGate;
            this.blockerSnapshotStarted = blockerSnapshotStarted;
        }

        public int GetCallCount(ChunkKey key, int version)
            => callCounts.GetValueOrDefault((key, version));

        public ValueTask<IChunkSnapshot?> TryCreateSnapshotAsync(ChunkKey key, int expectedVersion, CancellationToken ct)
        {
            callCounts.AddOrUpdate((key, expectedVersion), 1, static (_, c) => c + 1);

            if (key == blockerKey && expectedVersion == blockerVersion)
            {
                blockerSnapshotStarted.TrySetResult();
                blockerGate.Wait(ct);
            }

            return ValueTask.FromResult<IChunkSnapshot?>(new DummySnapshot(key, expectedVersion));
        }

        private sealed class DummySnapshot(ChunkKey key, int version) : IChunkSnapshot
        {
            public ChunkKey Key => key;

            public int Version => version;

            public int SizeX => 0;

            public int SizeY => 0;

            public int SizeZ => 0;

            public void Dispose()
            {
            }
        }
    }

    private sealed class TestStringProcessor(string id) : IChunkProcessor<string>
    {
        private int callCount;

        public string Id => id;

        public int CallCount => Volatile.Read(ref callCount);

        public ValueTask<string> ProcessAsync(IChunkSnapshot snapshot, CancellationToken ct)
        {
            Interlocked.Increment(ref callCount);
            return ValueTask.FromResult("artifact");
        }
    }
}
