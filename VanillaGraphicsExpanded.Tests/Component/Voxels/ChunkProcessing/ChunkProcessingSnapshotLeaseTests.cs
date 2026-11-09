using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

using VanillaGraphicsExpanded.Voxels.ChunkProcessing;

namespace VanillaGraphicsExpanded.Tests.Component.Voxels.ChunkProcessing;

/// <summary>Verifies that concurrent processors share one snapshot until their leases complete.</summary>
public sealed class ChunkProcessingSnapshotLeaseTests
{
    #region Public API

    /// <summary>Shares one snapshot across processors and disposes it after both processor leases finish.</summary>
    [Fact]
    [Trait("Category", "Unit")]
    public async Task Snapshot_Shared_Across_Processors_And_Disposed_Once_After_All_Leases_Complete()
    {
        // Arrange: configure two workers and hold both processors so the shared snapshot remains leased.
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

        // Act: request the same chunk version for two distinct processors.
        Task<ChunkWorkResult<string>> t1 = service.RequestAsync(key, 1, processor1, ct: testCt);
        Task<ChunkWorkResult<string>> t2 = service.RequestAsync(key, 1, processor2, ct: testCt);

        try
        {
            await snapshotSource.SnapshotCreated.Task.WaitAsync(TimeSpan.FromSeconds(1), testCt);
            await processor1.Started.Task.WaitAsync(TimeSpan.FromSeconds(1), testCt);
            await processor2.Started.Task.WaitAsync(TimeSpan.FromSeconds(1), testCt);

            // Assert: both processors have started with one shared snapshot, which is still in use.
            Assert.Equal(1, snapshotSource.CreateCount);
            Assert.NotNull(snapshotSource.LastSnapshot);
            Assert.Equal(0, snapshotSource.LastSnapshot!.DisposeCount);

            // Act: finish the first processor while the second processor continues holding its lease.
            proc1Gate.Set();
            ChunkWorkResult<string> r1 = await t1.WaitAsync(TimeSpan.FromSeconds(2), testCt);

            // Assert: one completed lease does not release the snapshot still used by the other processor.
            Assert.Equal(ChunkWorkStatus.Success, r1.Status);
            Assert.Equal(0, snapshotSource.LastSnapshot!.DisposeCount);

            // Act: finish the final processor and await its request.
            proc2Gate.Set();
            ChunkWorkResult<string> r2 = await t2.WaitAsync(TimeSpan.FromSeconds(2), testCt);

            // Assert: both requests succeed and the last lease releases the shared snapshot exactly once.
            Assert.Equal(ChunkWorkStatus.Success, r2.Status);
            Assert.Equal(1, snapshotSource.LastSnapshot!.DisposeCount);
        }
        finally
        {
            // Release both workers even when a timing assertion fails, then await bounded cleanup.
            proc1Gate.Set();
            proc2Gate.Set();
            await Task.WhenAll(t1, t2).WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    #endregion

    #region Private

    /// <summary>Provides controlled current versions for the snapshot-lease scenario.</summary>
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

    /// <summary>Creates and retains the single snapshot used by the two processor requests.</summary>
    private sealed class CountingSnapshotSource : IChunkSnapshotSource
    {
        #region Public API

        public int CreateCount => Volatile.Read(ref createCount);

        public TaskCompletionSource SnapshotCreated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CountingSnapshot? LastSnapshot { get; private set; }

        /// <summary>Creates a snapshot, records it, and signals that snapshot acquisition completed.</summary>
        public ValueTask<IChunkSnapshot?> TryCreateSnapshotAsync(ChunkKey key, int expectedVersion, CancellationToken ct)
        {
            Interlocked.Increment(ref createCount);

            var snap = new CountingSnapshot(key, expectedVersion);
            LastSnapshot = snap;
            SnapshotCreated.TrySetResult();

            return ValueTask.FromResult<IChunkSnapshot?>(snap);
        }

        #endregion

        #region Internal

        /// <summary>Tracks disposal calls while exposing the snapshot metadata to processors.</summary>
        internal sealed class CountingSnapshot(ChunkKey key, int version) : IChunkSnapshot
        {
            #region Public API

            public int DisposeCount => Volatile.Read(ref disposed);

            public ChunkKey Key => key;

            public int Version => version;

            public int SizeX => 0;

            public int SizeY => 0;

            public int SizeZ => 0;

            /// <summary>Records disposal and rejects a second release.</summary>
            public void Dispose()
            {
                if (Interlocked.Increment(ref disposed) != 1)
                {
                    throw new InvalidOperationException("Snapshot disposed more than once");
                }
            }

            #endregion

            #region Private

            private int disposed;

            #endregion
        }

        #endregion

        #region Private

        private int createCount;

        #endregion
    }

    /// <summary>Signals when processing starts, waits for release, and then returns a fixed artifact.</summary>
    private sealed class BlockingProcessor : IChunkProcessor<string>
    {
        #region Public API

        /// <summary>Creates a processor controlled by the supplied completion gate.</summary>
        public BlockingProcessor(string id, ManualResetEventSlim gate)
        {
            Id = id;
            this.gate = gate;
        }

        public string Id { get; }

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Signals entry, waits until the test releases the processor, and returns its artifact.</summary>
        public ValueTask<string> ProcessAsync(IChunkSnapshot snapshot, CancellationToken ct)
        {
            Started.TrySetResult();
            gate.Wait(ct);
            return ValueTask.FromResult("artifact");
        }

        #endregion

        #region Private

        private readonly ManualResetEventSlim gate;

        #endregion
    }

    #endregion
}
