using System;
using System.Threading;
using System.Threading.Tasks;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using VanillaGraphicsExpanded.WorldPartition;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.LumOn.Scene.NearField;

/// <summary>Game-world adapter using existing snapshot capture, workers, versions and bounded coalescing.</summary>
internal sealed class NearFieldWorldSource : INearFieldChunkSource, IDisposable
{
    private readonly ICoreClientAPI capi;
    private readonly LumonSceneTraceSceneChunkVersionProvider versions = new();
    private readonly NearFieldChunkProcessor processor = new();
    private readonly ChunkProcessingService processing;
    private readonly NearFieldChunkCache cache;
    private readonly NearFieldSourceLifetimes lifetimes;

    #region Lifetime and dependency updates
    /// <summary>Creates a source generation without sharing occupancy lifetime or configuration.</summary>
    public NearFieldWorldSource(ICoreClientAPI capi, NearFieldMaterialRegistry materials)
    {
        this.capi = capi;
        lifetimes = new(versions);
        var snapshots = new LumonSceneTraceSceneChunkSnapshotSource(capi, versions,
            new LumonSceneTraceSceneLightIdRegistry(), new LumonSceneTraceSceneMaterialPaletteRegistry(new LumonScenePbrSurfaceLutRegistry()),
            materials, nearFieldOnly: true);
        processing = new ChunkProcessingService(snapshots, versions, new() { WorkerCount = 2 });
        cache = new NearFieldChunkCache(LoadChunk, versions.GetCurrentVersion, IsAvailable);
    }

    /// <summary>Bounds source ownership and resets capture service for the new render update.</summary>
    public void BeginFrame(in PartitionCellRange cells)
    {
        lifetimes.Retain(cells);
        cache.BeginFrame(cells);
    }

    /// <summary>Records source changes safely even when game notifications arrive off the render thread.</summary>
    public void MarkDirty(ChunkKey key) => versions.MarkDirty(key);

    /// <summary>Cancels coalesced requests before releasing the existing worker infrastructure.</summary>
    public void Dispose()
    {
        cache.Dispose();
        processing.Dispose();
    }
    #endregion

    #region Source queries
    /// <summary>Provides shared immutable results without blocking the rendering thread on capture.</summary>
    public bool TryGet(ChunkKey key, out NearFieldChunkSnapshot? snapshot) => cache.TryGet(key, out snapshot);

    /// <summary>Validates both dependency revisions and loaded chunk lifetimes.</summary>
    public bool IsCurrent(ChunkKey key, int version) => cache.IsCurrent(key, version);

    /// <summary>Observes actual loaded chunk identity without forcing a chunk load.</summary>
    private bool IsAvailable(ChunkKey key)
    {
        if (capi.World == null) return lifetimes.Observe(key, null);
        key.Decode(out int x, out int y, out int z);
        var position = new BlockPos(0);
        position.Set(x * 32, y * 32, z * 32);
        IWorldChunk? chunk = capi.World.BlockAccessor.GetChunkAtBlockPos(position);
        return lifetimes.Observe(key, chunk is { Disposed: false } ? chunk : null);
    }

    /// <summary>Processes snapshot leases through the existing executor and only returns successful immutable artifacts.</summary>
    private async Task<NearFieldChunkSnapshot?> LoadChunk(ChunkKey key, int version, CancellationToken cancellation)
    {
        ChunkWorkResult<NearFieldChunkSnapshot> result = await processing.RequestAsync(key, version, processor, ct: cancellation).ConfigureAwait(false);
        return result.Status == ChunkWorkStatus.Success ? result.Artifact : null;
    }
    #endregion
}
