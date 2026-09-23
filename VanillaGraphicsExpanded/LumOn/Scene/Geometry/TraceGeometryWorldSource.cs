using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using VanillaGraphicsExpanded.WorldPartition;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.LumOn.Scene.Geometry;

/// <summary>Game adapter for the shared worker source; consumers never capture voxels themselves.</summary>
internal sealed class TraceGeometryWorldSource : ITraceGeometrySource
{
    private readonly ICoreClientAPI api;
    private readonly LumonSceneTraceSceneChunkVersionProvider versions = new();
    private readonly TraceGeometrySourceLifetimes lifetimes;
    private readonly ChunkProcessingService processing;
    private readonly TraceGeometryChunkProcessor processor = new();
    private readonly ConcurrentQueue<ChunkKey> dirty = new();
    public TraceGeometrySourceCache Cache { get; }

    /// <summary>Creates a generation-local executor with two workers and no secondary artifact retention.</summary>
    public TraceGeometryWorldSource(ICoreClientAPI api, TraceGeometryMaterials materials)
    {
        this.api = api; lifetimes = new(versions);
        var map = ((Vintagestory.Client.NoObf.ClientMain)api.World).WorldMap;
        var decoder = new TraceGeometryLightDecoder(map.BlockLightLevels, map.SunLightLevels, map.hueLevels, map.satLevels);
        var snapshots = new TraceGeometrySnapshotSource(api.World.BlockAccessor, api.World.GetBlock, versions, materials, decoder);
        processing = new(snapshots, versions, new() { WorkerCount = 2, ArtifactCacheBudgetBytes = 0 });
        Cache = new(Load, versions.GetCurrentVersion, Available);
    }

    #region Source lifetime
    /// <summary>Invalidates a source revision; cache cancellation runs on the owning render thread.</summary>
    public void MarkDirty(ChunkKey key) { versions.MarkDirty(key); dirty.Enqueue(key); }

    /// <summary>Bounds loaded-identity observations and resets edit retries on the owning thread.</summary>
    public void Prepare(TraceGeometryCoverage coverage)
    {
        lifetimes.Retain(new PartitionLayout(new(16, 16, 16)).Range(coverage.Window));
        while (dirty.TryDequeue(out var key)) Cache.Dirty(key);
    }

    /// <summary>Uses chunk identity to reject unload/reload even without a block edit notification.</summary>
    private bool Available(ChunkKey key)
    {
        key.Decode(out int x, out int y, out int z);
        if (api.World == null || y < 0 || y * 32L >= api.World.MapSizeY) return lifetimes.Observe(key, null);
        var chunk = api.World.BlockAccessor.GetChunkAtBlockPos(new BlockPos(x * 32, y * 32, z * 32));
        return lifetimes.Observe(key, chunk is { Disposed: false } ? chunk : null);
    }

    /// <summary>Returns only successful worker results; missing snapshots remain retryable.</summary>
    private async Task<TraceGeometryChunk?> Load(ChunkKey key, int version, CancellationToken cancellation)
    {
        var result = await processing.RequestAsync(key, version, processor, ct: cancellation).ConfigureAwait(false);
        return result.Status == ChunkWorkStatus.Success ? result.Artifact : null;
    }

    /// <summary>Cancels cache tasks before shutting down their executor.</summary>
    public void Dispose() { Cache.Dispose(); processing.Dispose(); }
    #endregion
}
