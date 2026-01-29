using System;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.LumOn.Scene;

internal enum LumonScenePageReleaseReason : byte
{
    ChunkUnloaded = 0,
    Evicted = 1,
    FieldTransition = 2,
    Reset = 3,
}

internal readonly record struct LumonScenePageReleasedEvent(
    LumonSceneField Field,
    LumonSceneChunkCoord Chunk,
    uint PhysicalPageId,
    LumonScenePageReleaseReason Reason);

/// <summary>
/// CPU-side wiring between chunk lifecycle and the global physical pools.
/// This layer is responsible for allocating and returning at least one physical page per active chunk,
/// and for evicting pages under pool pressure.
/// </summary>
internal sealed class LumonSceneChunkResidencyManager
{
    private readonly LumonScenePhysicalPoolManager pools;

    private readonly FieldState near;
    private readonly FieldState far;

    public event Action<LumonScenePageReleasedEvent>? PageReleased;

    public LumonSceneChunkResidencyManager(LumonScenePhysicalPoolManager pools)
    {
        this.pools = pools ?? throw new ArgumentNullException(nameof(pools));
        near = new FieldState(pools.Near);
        far = new FieldState(pools.Far);
    }

    public int ActiveChunksNear => near.ActiveChunks;
    public int ActiveChunksFar => far.ActiveChunks;

    public long EvictionsNear => near.EvictionCount;
    public long EvictionsFar => far.EvictionCount;

    public int FreePagesNear => pools.Near.PagePool.FreeCount;
    public int FreePagesFar => pools.Far.PagePool.FreeCount;

    public bool TryGetChunkPage(in LumonSceneChunkCoord chunk, out LumonSceneField field, out uint physicalPageId)
    {
        if (near.TryGetChunk(chunk, out physicalPageId))
        {
            field = LumonSceneField.Near;
            return true;
        }

        if (far.TryGetChunk(chunk, out physicalPageId))
        {
            field = LumonSceneField.Far;
            return true;
        }

        field = default;
        physicalPageId = 0;
        return false;
    }

    public bool TryActivateChunk(LumonSceneField field, in LumonSceneChunkCoord chunk, out LumonScenePhysicalPage page)
    {
        FieldState desired = field == LumonSceneField.Near ? near : far;
        FieldState other = field == LumonSceneField.Near ? far : near;

        if (desired.TryGetChunk(chunk, out uint existing))
        {
            desired.Touch(existing);
            desired.Decode(existing, out page);
            return true;
        }

        if (other.TryGetChunk(chunk, out uint otherPage))
        {
            other.Release(chunk, otherPage, LumonScenePageReleaseReason.FieldTransition, PageReleased);
        }

        if (desired.TryEnsureOnePage(chunk, out uint physicalPageId, out page))
        {
            return true;
        }

        // Try to evict and retry once.
        if (!desired.TryEvictOne(out uint evictedId, out bool hasOwner, out LumonSceneChunkCoord evictedOwner))
        {
            page = default;
            return false;
        }

        if (hasOwner)
        {
            desired.Release(evictedOwner, evictedId, LumonScenePageReleaseReason.Evicted, PageReleased);
        }
        else
        {
            desired.FreeUntracked(evictedId);
        }

        if (desired.TryEnsureOnePage(chunk, out physicalPageId, out page))
        {
            return true;
        }

        page = default;
        return false;
    }

    public void OnChunkUnloaded(in LumonSceneChunkCoord chunk)
    {
        if (near.TryGetChunk(chunk, out uint nearPage))
        {
            near.Release(chunk, nearPage, LumonScenePageReleaseReason.ChunkUnloaded, PageReleased);
        }

        if (far.TryGetChunk(chunk, out uint farPage))
        {
            far.Release(chunk, farPage, LumonScenePageReleaseReason.ChunkUnloaded, PageReleased);
        }
    }

    public void Reset()
    {
        near.ReleaseAll(LumonScenePageReleaseReason.Reset, PageReleased);
        far.ReleaseAll(LumonScenePageReleaseReason.Reset, PageReleased);
    }

    private sealed class FieldState
    {
        private readonly LumonScenePhysicalFieldPool pool;

        private readonly Dictionary<ulong, uint> chunkToPage = new();
        private readonly Dictionary<uint, ulong> pageToChunk = new();

        public int ActiveChunks => chunkToPage.Count;
        public long EvictionCount { get; private set; }

        public FieldState(LumonScenePhysicalFieldPool pool)
        {
            this.pool = pool ?? throw new ArgumentNullException(nameof(pool));
        }

        private bool PinGuaranteedPages
            // Only pin pages when the planner could actually satisfy the requested budget.
            => pool.Plan.CapacityPages >= pool.Plan.RequestedPages;

        public bool TryGetChunk(in LumonSceneChunkCoord chunk, out uint physicalPageId)
            => chunkToPage.TryGetValue(chunk.ToKey(), out physicalPageId);

        public void Touch(uint physicalPageId)
            => pool.PagePool.Touch(physicalPageId);

        public void Decode(uint physicalPageId, out LumonScenePhysicalPage page)
        {
            pool.PagePool.DecodePhysicalId(physicalPageId, out ushort a, out ushort x, out ushort y);
            page = new LumonScenePhysicalPage(physicalPageId, a, x, y);
        }

        public bool TryEnsureOnePage(in LumonSceneChunkCoord chunk, out uint physicalPageId, out LumonScenePhysicalPage page)
        {
            if (pool.TryAllocate(out page))
            {
                physicalPageId = page.PhysicalPageId;
                chunkToPage.Add(chunk.ToKey(), physicalPageId);
                pageToChunk.Add(physicalPageId, chunk.ToKey());

                if (PinGuaranteedPages)
                {
                    pool.PagePool.Pin(physicalPageId);
                }

                return true;
            }

            physicalPageId = 0;
            page = default;
            return false;
        }

        public bool TryEvictOne(out uint physicalPageId, out bool hasOwner, out LumonSceneChunkCoord owner)
        {
            if (!pool.TryGetEvictionCandidate(out physicalPageId))
            {
                hasOwner = false;
                owner = default;
                return false;
            }

            if (!pageToChunk.TryGetValue(physicalPageId, out ulong ownerKey))
            {
                // The page existed in the pool but isn't tracked as "chunk-owned" yet.
                // Still allow eviction by freeing it directly.
                hasOwner = false;
                owner = default;
                EvictionCount++;
                return true;
            }

            hasOwner = true;
            owner = LumonSceneChunkCoord.FromKey(ownerKey);
            EvictionCount++;
            return true;
        }

        public void FreeUntracked(uint physicalPageId)
        {
            if (PinGuaranteedPages)
            {
                pool.PagePool.Unpin(physicalPageId);
            }

            pool.Free(physicalPageId);
        }

        public void Release(in LumonSceneChunkCoord chunk, uint physicalPageId, LumonScenePageReleaseReason reason, Action<LumonScenePageReleasedEvent>? notify)
        {
            ulong chunkKey = chunk.ToKey();

            if (PinGuaranteedPages)
            {
                pool.PagePool.Unpin(physicalPageId);
            }

            chunkToPage.Remove(chunkKey);
            pageToChunk.Remove(physicalPageId);

            notify?.Invoke(new LumonScenePageReleasedEvent(pool.Field, chunk, physicalPageId, reason));

            pool.Free(physicalPageId);
        }

        public void ReleaseAll(LumonScenePageReleaseReason reason, Action<LumonScenePageReleasedEvent>? notify)
        {
            if (chunkToPage.Count == 0)
            {
                return;
            }

            // Copy keys to avoid modifying during enumeration.
            var chunks = new List<ulong>(chunkToPage.Keys);
            foreach (ulong chunkKey in chunks)
            {
                if (!chunkToPage.TryGetValue(chunkKey, out uint pageId))
                {
                    continue;
                }

                Release(LumonSceneChunkCoord.FromKey(chunkKey), pageId, reason, notify);
            }
        }
    }
}
