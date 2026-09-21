using VanillaGraphicsExpanded.LumOn.Scene.NearField;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.Tests.Fixtures.NearField;

/// <summary>Reusable delayed chunk loader and publication observer for pure lifecycle tests.</summary>
internal sealed class NearFieldGeometryFixture : INearFieldPublicationBackend, IDisposable
{
    public NearFieldChunkCache Cache { get; }
    public NearFieldGeometryPartition Provider { get; }
    public PartitionCoordinator Coordinator { get; }
    public long Instance { get; }
    public int Version { get; set; } = 1;
    public bool Available { get; set; } = true;
    public bool Deferred { get; set; }
    public NearFieldSourceCell[] Source { get; } = Enumerable.Repeat(new NearFieldSourceCell(1, default), 32768).ToArray();
    public List<(ChunkKey Key, int Version, CancellationToken Cancellation, TaskCompletionSource<NearFieldChunkSnapshot?> Completion)> Pending { get; } = new();
    public Dictionary<PartitionCellKey, NearFieldSourceCell[]> Published { get; } = new();
    public List<(ChunkKey Key, int Version)> SourceRequests { get; } = new();
    public long PublicationCount { get; private set; }
    public PartitionCellRange Window { get; set; } = new(new(0, 0, 0), new(2, 2, 2));

    #region Fixture lifecycle
    /// <summary>Connects the real cache, partition provider, and coordinator to observable storage.</summary>
    public NearFieldGeometryFixture(int maximumInFlight = 8, int capturesPerFrame = 2, double prefetchMargin = 0,
        PartitionLimits? limits = null)
    {
        Cache = new(Load, _ => Version, _ => Available, maximumInFlight, capturesPerFrame);
        Provider = new(Cache, this, new NearFieldMaterialRegistry());
        limits ??= new PartitionLimits(1000, 1000, 1000, 1000, long.MaxValue);
        Coordinator = new(limits);
        Instance = Coordinator.Register("near-field", "test", new(new(16,16,16)), new(prefetchMargin,prefetchMargin,0), limits, Provider);
        Coordinator.SetSource(new(1, Instance, "test", new(), new(new(), new(32,32,32))));
    }
    /// <summary>Retires coordinator-owned storage before cancelling pending chunk work.</summary>
    public void Dispose() { Coordinator.Unregister(Instance); Cache.Dispose(); }
    /// <summary>Executes dependency invalidation before capture and publication.</summary>
    public void Frame(long tick) { Cache.BeginFrame(Window); Provider.RefreshDependencies(Coordinator); Coordinator.Pump(tick); }
    /// <summary>Copies immediate input or exposes an explicitly delayed source operation.</summary>
    private Task<NearFieldChunkSnapshot?> Load(ChunkKey key, int version, CancellationToken cancellation)
    {
        SourceRequests.Add((key, version));
        if (!Deferred) return Task.FromResult<NearFieldChunkSnapshot?>(new(key, version, Source));
        var completion = new TaskCompletionSource<NearFieldChunkSnapshot?>();
        Pending.Add((key,version,cancellation,completion));
        return completion.Task;
    }
    /// <summary>Completes a selected capture with the revision it originally observed.</summary>
    public void Complete(int index) { var work = Pending[index]; work.Completion.SetResult(new(work.Key, work.Version, Source)); }
    #endregion

    #region Publication backend
    /// <summary>Tests fixed-grid membership of the current bounded window.</summary>
    public bool ContainsCell(in PartitionCoordinate c) => c.X >= Window.Min.X && c.Y >= Window.Min.Y && c.Z >= Window.Min.Z && c.X < Window.End.X && c.Y < Window.End.Y && c.Z < Window.End.Z;
    /// <summary>Accepts only addressable publication requests.</summary>
    public bool ClaimCell(PartitionRequest request) => ContainsCell(request.Key.Coordinate);
    /// <summary>Copies acknowledged payloads so assertions observe coherent storage.</summary>
    public bool PublishCell(PartitionRequest request, ReadOnlySpan<NearFieldSourceCell> cells, NearFieldMaterialRegistry materials)
    {
        Published[request.Key] = cells.ToArray();
        PublicationCount++;
        return true;
    }
    /// <summary>Hides content immediately when its dependency becomes stale.</summary>
    public void InvalidateCell(in PartitionCellKey key) => Published.Remove(key);
    /// <summary>Releases storage for departed cells.</summary>
    public void RetireCell(in PartitionCellKey key) => Published.Remove(key);
    #endregion
}
