using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

using VanillaGraphicsExpanded.Voxels.ChunkProcessing;

namespace VanillaGraphicsExpanded.Tests.Component.Voxels.ChunkProcessing;

/// <summary>Verifies queued chunk requests are superseded before they acquire a snapshot.</summary>
public sealed class ChunkProcessingQueueSupersessionTests
{
    #region Public API

    /// <summary>Rejects an older queued version without snapshotting it, then processes the newer version.</summary>
    [Fact]
    [Trait("Category", "Unit")]
    public async Task Enqueue_Newer_Version_Supersedes_Older_Queued_Immediately_Without_Snapshot()
    {
        // Arrange: occupy the only worker so both requests for the target chunk remain queued.
        CancellationToken testCt = TestContext.Current.CancellationToken;

        var key = ChunkKey.FromChunkCoords(1, 2, 3);
        var blockerKey = ChunkKey.FromChunkCoords(100, 100, 100);

        var versionProvider = new TestChunkVersionProvider();
        versionProvider.SetCurrentVersion(key, 1);
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

        try
        {
            await blockerSnapshotStarted.Task.WaitAsync(TimeSpan.FromSeconds(1), testCt);
        }
        catch (TimeoutException error)
        {
            // Preserve the startup deadline and report scheduling state only after it fails.
            ThreadPool.GetAvailableThreads(out int availableWorkers, out int availableIo);
            throw new TimeoutException($"Blocker snapshot startup deadline expired: request={blockerTask.Status}, "
                + $"snapshotCalls={snapshotSource.GetCallCount(blockerKey, 1)}, poolThreads={ThreadPool.ThreadCount}, "
                + $"poolPending={ThreadPool.PendingWorkItemCount}, availableWorkers={availableWorkers}, availableIo={availableIo}.", error);
        }

        var processor = new TestStringProcessor(id: "TestProc");

        // Act: enqueue two versions while the worker remains blocked on the unrelated chunk.
        Task<ChunkWorkResult<string>> v1Task = service.RequestAsync(key, 1, processor, ct: testCt);

        // Assert: version one remains queued because the only worker is still occupied.
        Assert.False(v1Task.IsCompleted);

        // Act: advance the current version and enqueue its replacement.
        versionProvider.SetCurrentVersion(key, 2);
        Task<ChunkWorkResult<string>> v2Task = service.RequestAsync(key, 2, processor, ct: testCt);

        ChunkWorkResult<string> v1Result = await v1Task.WaitAsync(TimeSpan.FromMilliseconds(250), testCt);

        // Assert: supersession is immediate and does not create the obsolete snapshot.
        Assert.Equal(ChunkWorkStatus.Superseded, v1Result.Status);
        Assert.Equal(0, snapshotSource.GetCallCount(key, 1));

        // Act: release the worker so the current request can finish.
        blockerGate.Set();

        ChunkWorkResult<string> v2Result = await v2Task.WaitAsync(TimeSpan.FromSeconds(2), testCt);

        // Assert: only the current version reaches snapshotting and processing.
        Assert.Equal(ChunkWorkStatus.Success, v2Result.Status);
        Assert.Equal("artifact", v2Result.Artifact);

        Assert.Equal(1, snapshotSource.GetCallCount(key, 2));
        Assert.Equal(1, processor.CallCount);

        _ = await blockerTask.WaitAsync(TimeSpan.FromSeconds(2), testCt);
    }

    #endregion

    #region Private

    /// <summary>Provides controlled current versions for the queued-request scenario.</summary>
    private sealed class TestChunkVersionProvider : IChunkVersionProvider
    {
        #region Public API

        /// <summary>Returns the configured current version, or zero when no version was configured.</summary>
        public int GetCurrentVersion(ChunkKey key) => currentVersion.GetValueOrDefault(key);

        /// <summary>Sets the current version for a chunk.</summary>
        public void SetCurrentVersion(ChunkKey key, int version) => currentVersion[key] = version;

        #endregion

        #region Private

        private readonly ConcurrentDictionary<ChunkKey, int> currentVersion = new();

        #endregion
    }

    /// <summary>Counts snapshot requests and blocks snapshot creation for one selected chunk version.</summary>
    private sealed class BlockingSnapshotSource : IChunkSnapshotSource
    {
        #region Public API

        /// <summary>Creates a source that blocks only the selected chunk version before returning its snapshot.</summary>
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

        /// <summary>Returns how many times the requested chunk version was snapshotted.</summary>
        public int GetCallCount(ChunkKey key, int version)
            => callCounts.GetValueOrDefault((key, version));

        /// <summary>Records the request, signals and blocks the selected version, then returns a dummy snapshot.</summary>
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

        #endregion

        #region Private

        private readonly ChunkKey blockerKey;
        private readonly int blockerVersion;
        private readonly ManualResetEventSlim blockerGate;
        private readonly TaskCompletionSource blockerSnapshotStarted;
        private readonly ConcurrentDictionary<(ChunkKey Key, int Version), int> callCounts = new();

        /// <summary>Supplies the identity and version metadata required by the processing service.</summary>
        private sealed class DummySnapshot(ChunkKey key, int version) : IChunkSnapshot
        {
            #region Public API

            public ChunkKey Key => key;

            public int Version => version;

            public int SizeX => 0;

            public int SizeY => 0;

            public int SizeZ => 0;

            /// <summary>Releases no resources because this test snapshot contains only scalar metadata.</summary>
            public void Dispose()
            {
            }

            #endregion
        }

        #endregion
    }

    /// <summary>Counts processing calls while returning a fixed artifact.</summary>
    private sealed class TestStringProcessor(string id) : IChunkProcessor<string>
    {
        #region Public API

        public string Id => id;

        public int CallCount => Volatile.Read(ref callCount);

        /// <summary>Records the invocation and returns the deterministic test artifact.</summary>
        public ValueTask<string> ProcessAsync(IChunkSnapshot snapshot, CancellationToken ct)
        {
            Interlocked.Increment(ref callCount);
            return ValueTask.FromResult("artifact");
        }

        #endregion

        #region Private

        private int callCount;

        #endregion
    }

    #endregion
}
