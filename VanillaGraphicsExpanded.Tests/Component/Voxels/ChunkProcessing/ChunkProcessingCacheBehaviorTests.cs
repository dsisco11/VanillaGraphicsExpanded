using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

using VanillaGraphicsExpanded.Voxels.ChunkProcessing;

namespace VanillaGraphicsExpanded.Tests.Component.Voxels.ChunkProcessing;

/// <summary>Verifies cache hits and prevents stale or failed work from publishing cached artifacts.</summary>
public sealed class ChunkProcessingCacheBehaviorTests
{
    #region Public API

    /// <summary>Returns a cached artifact without creating another snapshot or invoking the processor again.</summary>
    [Fact]
    [Trait("Category", "Unit")]
    public async Task CacheHit_Returns_Without_Snapshot_Or_Processor()
    {
        // Arrange: configure a valid chunk version and a service with artifact caching enabled.
        CancellationToken testCt = TestContext.Current.CancellationToken;

        var key = ChunkKey.FromChunkCoords(1, 1, 1);

        var versionProvider = new TestChunkVersionProvider();
        versionProvider.SetCurrentVersion(key, 1);

        var snapshotSource = new CountingSnapshotSource();
        var processor = new CountingProcessor("Proc");

        using var service = new ChunkProcessingService(snapshotSource, versionProvider, new ChunkProcessingServiceOptions
        {
            WorkerCount = 1,
            ArtifactCacheBudgetBytes = 32 * 1024 * 1024,
        });

        // Act: process the version once.
        ChunkWorkResult<string> first = await service.RequestAsync(key, 1, processor, ct: testCt)
            .WaitAsync(TimeSpan.FromSeconds(2), testCt);

        // Assert: the first request produces the artifact through the snapshot and processor.
        Assert.Equal(ChunkWorkStatus.Success, first.Status);
        Assert.Equal("artifact", first.Artifact);
        Assert.Equal(1, snapshotSource.CreateCount);
        Assert.Equal(1, processor.CallCount);

        // Act: request the same version again.
        ChunkWorkResult<string> second = await service.RequestAsync(key, 1, processor, ct: testCt)
            .WaitAsync(TimeSpan.FromMilliseconds(250), testCt);

        // Assert: the cache returns the artifact without repeating snapshot or processor work.
        Assert.Equal(ChunkWorkStatus.Success, second.Status);
        Assert.Equal("artifact", second.Artifact);
        Assert.Equal(1, snapshotSource.CreateCount);
        Assert.Equal(1, processor.CallCount);
    }

    /// <summary>Rejects a result made stale during processing and verifies that the stale artifact was not cached.</summary>
    [Fact]
    [Trait("Category", "Unit")]
    public async Task StalePublish_Completes_Superseded_And_DoesNotCache()
    {
        // Arrange: make the result stale when processing starts, before it can be published.
        CancellationToken testCt = TestContext.Current.CancellationToken;

        var key = ChunkKey.FromChunkCoords(2, 2, 2);

        var versionProvider = new TestChunkVersionProvider();
        versionProvider.SetCurrentVersion(key, 1);

        var snapshotSource = new CountingSnapshotSource();

        var processor = new ProcessorThatMakesChunkStale("Proc", onStart: () => versionProvider.SetCurrentVersion(key, 2));

        using var service = new ChunkProcessingService(snapshotSource, versionProvider, new ChunkProcessingServiceOptions
        {
            WorkerCount = 1,
            ArtifactCacheBudgetBytes = 32 * 1024 * 1024,
        });

        // Act: process the version while the processor changes the current version.
        ChunkWorkResult<string> first = await service.RequestAsync(key, 1, processor, ct: testCt)
            .WaitAsync(TimeSpan.FromSeconds(2), testCt);

        // Assert: the service reports supersession after processing the now-stale result.
        Assert.Equal(ChunkWorkStatus.Superseded, first.Status);
        Assert.Equal(1, snapshotSource.CreateCount);
        Assert.Equal(1, processor.CallCount);

        // If it had cached despite staleness, this second request would be a fast-path cache hit.
        // Act: restore the version and repeat the request with an observable processor.
        versionProvider.SetCurrentVersion(key, 1);

        var processor2 = new CountingProcessor("Proc");
        ChunkWorkResult<string> second = await service.RequestAsync(key, 1, processor2, ct: testCt)
            .WaitAsync(TimeSpan.FromSeconds(2), testCt);

        // Assert: the second processor runs, proving the stale artifact was not cached.
        Assert.Equal(ChunkWorkStatus.Success, second.Status);
        Assert.Equal(2, snapshotSource.CreateCount);
        Assert.Equal(1, processor2.CallCount);
    }

    /// <summary>Returns a processor failure and verifies a later request is not hidden by a cached artifact.</summary>
    [Fact]
    [Trait("Category", "Unit")]
    public async Task ProcessorException_Returns_Failed_And_DoesNotCache()
    {
        // Arrange: configure a throwing processor and a successful processor for the observable retry.
        CancellationToken testCt = TestContext.Current.CancellationToken;

        var key = ChunkKey.FromChunkCoords(3, 3, 3);

        var versionProvider = new TestChunkVersionProvider();
        versionProvider.SetCurrentVersion(key, 1);

        var snapshotSource = new CountingSnapshotSource();

        var processor = new ThrowingProcessor("Proc", new InvalidOperationException("boom"));

        using var service = new ChunkProcessingService(snapshotSource, versionProvider, new ChunkProcessingServiceOptions
        {
            WorkerCount = 1,
            ArtifactCacheBudgetBytes = 32 * 1024 * 1024,
        });

        // Act: process the chunk with the throwing processor.
        ChunkWorkResult<string> first = await service.RequestAsync(key, 1, processor, ct: testCt)
            .WaitAsync(TimeSpan.FromSeconds(2), testCt);

        // Assert: the processor error is represented in the service result.
        Assert.Equal(ChunkWorkStatus.Failed, first.Status);
        Assert.Equal(ChunkWorkError.ProcessorFailed, first.Error);
        Assert.Contains("boom", first.Reason);

        // Ensure no cache: a successful processor with same id should still run.
        var processor2 = new CountingProcessor("Proc");

        // Act: retry the same version with the successful processor.
        ChunkWorkResult<string> second = await service.RequestAsync(key, 1, processor2, ct: testCt)
            .WaitAsync(TimeSpan.FromSeconds(2), testCt);

        // Assert: the retry performs fresh work and succeeds.
        Assert.Equal(ChunkWorkStatus.Success, second.Status);
        Assert.Equal(2, snapshotSource.CreateCount);
        Assert.Equal(1, processor2.CallCount);
    }

    #endregion

    #region Private

    /// <summary>Provides controlled current versions for cache and publication scenarios.</summary>
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

    /// <summary>Counts snapshot requests and supplies deterministic metadata-only snapshots.</summary>
    private sealed class CountingSnapshotSource : IChunkSnapshotSource
    {
        #region Public API

        public int CreateCount => Volatile.Read(ref createCount);

        /// <summary>Increments the snapshot count and returns a metadata-only snapshot.</summary>
        public ValueTask<IChunkSnapshot?> TryCreateSnapshotAsync(ChunkKey key, int expectedVersion, CancellationToken ct)
        {
            Interlocked.Increment(ref createCount);
            return ValueTask.FromResult<IChunkSnapshot?>(new DummySnapshot(key, expectedVersion));
        }

        #endregion

        #region Private

        private int createCount;

        /// <summary>Supplies the chunk identity and version required by the processing service.</summary>
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

    /// <summary>Counts processor calls while returning a fixed artifact.</summary>
    private sealed class CountingProcessor(string id) : IChunkProcessor<string>
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

    /// <summary>Changes the chunk version at processor entry to make its artifact stale before publication.</summary>
    private sealed class ProcessorThatMakesChunkStale(string id, Action onStart) : IChunkProcessor<string>
    {
        #region Public API

        public string Id => id;

        public int CallCount => Volatile.Read(ref callCount);

        /// <summary>Records the invocation, marks the chunk stale, and returns the test artifact.</summary>
        public ValueTask<string> ProcessAsync(IChunkSnapshot snapshot, CancellationToken ct)
        {
            Interlocked.Increment(ref callCount);
            onStart();
            return ValueTask.FromResult("artifact");
        }

        #endregion

        #region Private

        private int callCount;

        #endregion
    }

    /// <summary>Always throws the configured exception when invoked by the service.</summary>
    private sealed class ThrowingProcessor(string id, Exception ex) : IChunkProcessor<string>
    {
        #region Public API

        public string Id => id;

        /// <summary>Throws the configured processor exception.</summary>
        public ValueTask<string> ProcessAsync(IChunkSnapshot snapshot, CancellationToken ct)
            => throw ex;

        #endregion
    }

    #endregion
}
