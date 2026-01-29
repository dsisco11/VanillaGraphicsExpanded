using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

using VanillaGraphicsExpanded.Voxels.ChunkProcessing;

namespace VanillaGraphicsExpanded.Tests.Unit;

public sealed class ChunkProcessingServicePhase4Tests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task CacheHit_Returns_Without_Snapshot_Or_Processor()
    {
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

        ChunkWorkResult<string> first = await service.RequestAsync(key, 1, processor, ct: testCt)
            .WaitAsync(TimeSpan.FromSeconds(2), testCt);

        Assert.Equal(ChunkWorkStatus.Success, first.Status);
        Assert.Equal("artifact", first.Artifact);
        Assert.Equal(1, snapshotSource.CreateCount);
        Assert.Equal(1, processor.CallCount);

        ChunkWorkResult<string> second = await service.RequestAsync(key, 1, processor, ct: testCt)
            .WaitAsync(TimeSpan.FromMilliseconds(250), testCt);

        Assert.Equal(ChunkWorkStatus.Success, second.Status);
        Assert.Equal("artifact", second.Artifact);
        Assert.Equal(1, snapshotSource.CreateCount);
        Assert.Equal(1, processor.CallCount);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task StalePublish_Completes_Superseded_And_DoesNotCache()
    {
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

        ChunkWorkResult<string> first = await service.RequestAsync(key, 1, processor, ct: testCt)
            .WaitAsync(TimeSpan.FromSeconds(2), testCt);

        Assert.Equal(ChunkWorkStatus.Superseded, first.Status);
        Assert.Equal(1, snapshotSource.CreateCount);
        Assert.Equal(1, processor.CallCount);

        // If it had cached despite staleness, this second request would be a fast-path cache hit.
        versionProvider.SetCurrentVersion(key, 1);

        var processor2 = new CountingProcessor("Proc");
        ChunkWorkResult<string> second = await service.RequestAsync(key, 1, processor2, ct: testCt)
            .WaitAsync(TimeSpan.FromSeconds(2), testCt);

        Assert.Equal(ChunkWorkStatus.Success, second.Status);
        Assert.Equal(2, snapshotSource.CreateCount);
        Assert.Equal(1, processor2.CallCount);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ProcessorException_Returns_Failed_And_DoesNotCache()
    {
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

        ChunkWorkResult<string> first = await service.RequestAsync(key, 1, processor, ct: testCt)
            .WaitAsync(TimeSpan.FromSeconds(2), testCt);

        Assert.Equal(ChunkWorkStatus.Failed, first.Status);
        Assert.Equal(ChunkWorkError.ProcessorFailed, first.Error);
        Assert.Contains("boom", first.Reason);

        // Ensure no cache: a successful processor with same id should still run.
        var processor2 = new CountingProcessor("Proc");
        ChunkWorkResult<string> second = await service.RequestAsync(key, 1, processor2, ct: testCt)
            .WaitAsync(TimeSpan.FromSeconds(2), testCt);

        Assert.Equal(ChunkWorkStatus.Success, second.Status);
        Assert.Equal(2, snapshotSource.CreateCount);
        Assert.Equal(1, processor2.CallCount);
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

        public ValueTask<IChunkSnapshot?> TryCreateSnapshotAsync(ChunkKey key, int expectedVersion, CancellationToken ct)
        {
            Interlocked.Increment(ref createCount);
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

    private sealed class CountingProcessor(string id) : IChunkProcessor<string>
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

    private sealed class ProcessorThatMakesChunkStale(string id, Action onStart) : IChunkProcessor<string>
    {
        private int callCount;

        public string Id => id;

        public int CallCount => Volatile.Read(ref callCount);

        public ValueTask<string> ProcessAsync(IChunkSnapshot snapshot, CancellationToken ct)
        {
            Interlocked.Increment(ref callCount);
            onStart();
            return ValueTask.FromResult("artifact");
        }
    }

    private sealed class ThrowingProcessor(string id, Exception ex) : IChunkProcessor<string>
    {
        public string Id => id;

        public ValueTask<string> ProcessAsync(IChunkSnapshot snapshot, CancellationToken ct)
            => throw ex;
    }
}
