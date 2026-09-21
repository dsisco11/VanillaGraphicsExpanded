using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.Tests.Fixtures.NearField;

/// <summary>Controlled shared-source completions and observable storage through the production partition.</summary>
internal sealed class TraceGeometryFixture : IDisposable
{
    public PartitionCoordinator Coordinator { get; }
    public TraceGeometrySourceCache Cache { get; }
    public TraceGeometryPartition Partition { get; }
    public RecordingBackend Backend { get; }
    public Dictionary<ChunkKey, int> Versions { get; } = new();
    public List<(ChunkKey Key, int Version, TaskCompletionSource<TraceGeometryChunk?> Completion)> Requests { get; } = new();
    private long tick;

    /// <summary>Creates a deterministic backend without replacing coordinator publication decisions.</summary>
    public TraceGeometryFixture(int resolution = 48, long uploadLimit = 8 * 1024 * 1024, int captureLimit = 128, int inFlightLimit = 256)
    {
        Coordinator = new(new(16384, inFlightLimit, captureLimit, 128, uploadLimit));
        Backend = new(resolution);
        Cache = new((key, version, _) =>
        {
            var tcs = new TaskCompletionSource<TraceGeometryChunk?>();
            Requests.Add((key, version, tcs)); return tcs.Task;
        }, key => Versions.GetValueOrDefault(key), _ => true);
        Partition = new(Coordinator, Cache, new(), Backend);
    }

    /// <summary>Follows the host's preparation/pump/service order.</summary>
    public void Frame(TraceGeometryCoverage plan)
    { Partition.Prepare(plan); Coordinator.Pump(++tick); Partition.Service(plan); }

    /// <summary>Completes currently captured requests with a recognizable source revision.</summary>
    public void Complete(uint kind = 1)
    {
        foreach (var request in Requests.Where(r => !r.Completion.Task.IsCompleted))
            request.Completion.SetResult(new(request.Key, request.Version,
                Enumerable.Repeat(new TraceGeometryVoxel(kind, 7, 0x44332211), 32768).ToArray()));
    }

    /// <summary>Retires shared ownership before disposing the fixture.</summary>
    public void Dispose() => Partition.Dispose();

    /// <summary>Tracks only coordinator-authorized publications and owner-specific invalidations.</summary>
    internal sealed class RecordingBackend : ITraceGeometryBackend
    {
        public int Resolution { get; }
        public long TablesRevision { get; private set; } = -1;
        public Dictionary<PartitionCellKey, TraceGeometryCell> Ready { get; } = new();
        public List<PartitionCellKey> Publications { get; } = new();
        public long UploadedBytes { get; private set; }
        /// <summary>Uses the requested physical capacity.</summary>
        public RecordingBackend(int resolution) => Resolution = resolution;
        /// <summary>Coverage ownership remains in the coordinator in this logical backend.</summary>
        public void SetWindow(TraceGeometryCoverage coverage) { }
        /// <summary>Commits material readiness only after the last staged range.</summary>
        public void UploadTableRange(TraceGeometryTables tables, int offset, int bytes)
        { UploadedBytes += bytes; if (offset + bytes == TraceGeometryTables.MaximumUploadBytes) TablesRevision = tables.Revision; }
        /// <summary>Records content only when called by the real coordinator path.</summary>
        public bool Publish(PartitionRequest request, TraceGeometryCell cell, TraceGeometryTables tables)
        { Assert.Equal(TablesRevision, tables.Revision); Ready[request.Key] = cell; Publications.Add(request.Key); UploadedBytes += TraceGeometryCell.UploadBytes + 2; return true; }
        /// <summary>Removes one exact logical owner.</summary>
        public void Invalidate(in PartitionCellKey key) => Ready.Remove(key);
        /// <summary>Clears remaining observations.</summary>
        public void Dispose() => Ready.Clear();
    }
}
