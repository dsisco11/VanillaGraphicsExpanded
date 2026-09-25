using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Supplies controlled chunks while the production renderer owns planning, scheduling and publication.</summary>
internal sealed class RuntimeTraceGeometrySource : ITraceGeometrySource
{
    private readonly Func<int, int, int, TraceGeometryVoxel> sample;
    private int version;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<ChunkKey,int> chunkVersions=new();
    public TraceGeometrySourceCache Cache { get; }
    public bool Disposed { get; private set; }
    /// <summary>Counts controlled whole-chunk snapshots requested by production scheduling.</summary>
    public int CaptureCount { get; private set; }

    #region Source lifetime
    /// <summary>Connects deterministic chunk capture to the production source cache.</summary>
    public RuntimeTraceGeometrySource(Func<int, int, int, TraceGeometryVoxel> sample, Func<bool>? available = null, Func<ChunkKey,bool>? loaded = null)
    {
        this.sample = sample;
        Cache = new(Capture, key => version+(chunkVersions.TryGetValue(key,out int local)?local:0), key => (available?.Invoke() ?? true) && (loaded?.Invoke(key) ?? true));
    }

    /// <summary>Copies whole chunks using the same cell ordering as the game source adapter.</summary>
    private Task<TraceGeometryChunk?> Capture(ChunkKey key, int revision, CancellationToken cancellation)
    {
        CaptureCount++;
        key.Decode(out int cx, out int cy, out int cz);
        var data = new TraceGeometryVoxel[32768];
        for (int y = 0; y < 32; y++) for (int z = 0; z < 32; z++) for (int x = 0; x < 32; x++)
            data[(y * 32 + z) * 32 + x] = sample(cx * 32 + x, cy * 32 + y, cz * 32 + z);
        return Task.FromResult<TraceGeometryChunk?>(new(key, revision, data));
    }

    /// <summary>The controlled world has all requested source identities loaded from initialization.</summary>
    public void Prepare(TraceGeometryCoverage coverage) { }
    /// <summary>Advances source identity when the engine reports an edit.</summary>
    public void MarkDirty(ChunkKey key) => version++;
    /// <summary>Versions only one source chunk so tests can isolate unrelated publication from global fixture edits.</summary>
    public void MarkChunkDirty(ChunkKey key) => chunkVersions.AddOrUpdate(key,1,(_,old)=>old+1);
    /// <summary>Releases cached chunks on production renderer teardown.</summary>
    public void Dispose() { Disposed = true; Cache.Dispose(); }
    #endregion
}
