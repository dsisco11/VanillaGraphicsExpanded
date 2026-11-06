using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.Tests.Fixtures.NearField;

/// <summary>Controls whether test observations retain payloads or only publication identity.</summary>
internal enum TraceGeometryObservationMode { FullPayloads, MetadataOnly }

/// <summary>Controlled shared-source completions and observable storage through the production partition.</summary>
internal sealed class TraceGeometryFixture : IDisposable
{
    #region Private
    private static readonly TraceGeometryVoxel[] uniformSourceVoxels =
        Enumerable.Repeat(new TraceGeometryVoxel(1, 7, 0x44332211), 32768).ToArray();
    private readonly TraceGeometryObservationMode observationMode;
    private readonly List<(ChunkKey Key, int Version, TaskCompletionSource<TraceGeometryChunk?> Completion)> pendingRequests = new();
    private long tick;
    #endregion

    #region Public API
    public PartitionCoordinator Coordinator { get; }
    public TraceGeometrySourceCache Cache { get; }
    public TraceGeometryPartition Partition { get; }
    public RecordingBackend Backend { get; }
    public Dictionary<ChunkKey, int> Versions { get; } = new();
    public List<(ChunkKey Key, int Version, TaskCompletionSource<TraceGeometryChunk?> Completion)> Requests { get; } = new();
    public List<(ChunkKey Key, int Version)> RequestHistory { get; } = new();

    /// <summary>Creates a deterministic backend without replacing coordinator publication decisions.</summary>
    public TraceGeometryFixture(int resolution = 48, long uploadLimit = 8 * 1024 * 1024, int captureLimit = 128,
        int inFlightLimit = 256, TraceGeometryObservationMode observationMode = TraceGeometryObservationMode.FullPayloads)
    {
        this.observationMode = observationMode;
        Coordinator = new(new(16384, inFlightLimit, captureLimit, 128, uploadLimit));
        Backend = new(resolution, observationMode == TraceGeometryObservationMode.FullPayloads);
        Cache = new((key, version, _) =>
        {
            var tcs = new TaskCompletionSource<TraceGeometryChunk?>();
            var request = (key, version, tcs);
            if (observationMode == TraceGeometryObservationMode.FullPayloads) Requests.Add(request);
            else { RequestHistory.Add((key, version)); pendingRequests.Add(request); }
            return tcs.Task;
        }, key => Versions.GetValueOrDefault(key), _ => true);
        Partition = new(Coordinator, Cache, new(), Backend);
    }

    /// <summary>Follows the host's preparation/pump/service order.</summary>
    public void Frame(TraceGeometryCoverage plan, TraceGeometryWorkBudget? budget = null)
    {
        Partition.Prepare(plan); Coordinator.Pump(++tick); Partition.Service(plan, budget);
        if (observationMode == TraceGeometryObservationMode.MetadataOnly)
            pendingRequests.RemoveAll(request => request.Completion.Task.IsCompleted);
    }

    /// <summary>Completes currently captured requests with a recognizable source revision.</summary>
    public void Complete(uint kind = 1)
    {
        var observedRequests = observationMode == TraceGeometryObservationMode.FullPayloads ? Requests : pendingRequests;
        foreach (var request in observedRequests.Where(request => !request.Completion.Task.IsCompleted))
        {
            // The chunk constructor copies its input, so this private uniform source can be safely reused.
            var chunk = new TraceGeometryChunk(request.Key, request.Version,
                kind == 1 ? uniformSourceVoxels : Enumerable.Repeat(new TraceGeometryVoxel(kind, 7, 0x44332211), 32768).ToArray());
            request.Completion.SetResult(chunk);
        }
    }

    /// <summary>Retires shared ownership before disposing the fixture.</summary>
    public void Dispose() => Partition.Dispose();
    #endregion

    #region Internal observations
    /// <summary>Tracks only coordinator-authorized publications and owner-specific invalidations.</summary>
    internal sealed class RecordingBackend : ITraceGeometryBackend
    {
        #region Private
        private readonly bool retainCellPayloads;
        #endregion

        #region Public API
        public int Resolution { get; }
        public long TablesRevision { get; private set; } = -1;
        public Dictionary<PartitionCellKey, TraceGeometryCell> Ready { get; } = new();
        public HashSet<PartitionCellKey> PublishedKeys { get; } = new();
        public List<PartitionCellKey> Publications { get; } = new();
        public long UploadedBytes { get; private set; }
        /// <summary>Reports current logical publication count without retaining cells in metadata mode.</summary>
        public int PublishedCount => retainCellPayloads ? Ready.Count : PublishedKeys.Count;
        /// <summary>Uses the requested physical capacity.</summary>
        public RecordingBackend(int resolution, bool retainCellPayloads = true)
        { Resolution = resolution; this.retainCellPayloads = retainCellPayloads; }
        /// <summary>Coverage ownership remains in the coordinator in this logical backend.</summary>
        public void SetWindow(TraceGeometryCoverage coverage) { }
        /// <summary>Commits material readiness only after the last staged range.</summary>
        public void UploadTableRange(TraceGeometryTables tables, int offset, int bytes)
        { UploadedBytes += bytes; if (offset + bytes == TraceGeometryTables.MaximumUploadBytes) TablesRevision = tables.Revision; }
        /// <summary>Records content only when called by the real coordinator path.</summary>
        public bool Publish(PartitionRequest request, TraceGeometryCell cell, TraceGeometryTables tables)
        {
            Assert.Equal(TablesRevision, tables.Revision);
            if (retainCellPayloads) Ready[request.Key] = cell;
            else PublishedKeys.Add(request.Key);
            Publications.Add(request.Key); UploadedBytes += TraceGeometryCell.UploadBytes + 2; return true;
        }
        /// <summary>Removes one exact logical owner.</summary>
        public void Invalidate(in PartitionCellKey key) { Ready.Remove(key); PublishedKeys.Remove(key); }
        /// <summary>Clears remaining observations.</summary>
        public void Dispose() { Ready.Clear(); PublishedKeys.Clear(); }
        #endregion
    }
    #endregion
}
