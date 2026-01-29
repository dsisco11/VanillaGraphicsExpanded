using System;

namespace VanillaGraphicsExpanded.LumOn.LumonScene;

internal readonly struct LumonScenePhysicalPage
{
    public readonly uint PhysicalPageId;
    public readonly ushort AtlasIndex;
    public readonly ushort TileX;
    public readonly ushort TileY;

    public LumonScenePhysicalPage(uint physicalPageId, ushort atlasIndex, ushort tileX, ushort tileY)
    {
        PhysicalPageId = physicalPageId;
        AtlasIndex = atlasIndex;
        TileX = tileX;
        TileY = tileY;
    }
}

/// <summary>
/// Budgeted physical page pool for a single field (Near or Far).
/// Pages are backed by a fixed set of 4096x4096 atlas textures; each physical page corresponds to one tile in an atlas layer.
/// </summary>
internal sealed class LumonScenePhysicalPagePool
{
    private readonly int tileSizeTexels;
    private readonly int tilesPerAxis;
    private readonly int tilesPerAtlas;
    private readonly int atlasCount;
    private readonly int capacityPages;

    private readonly int[] freeStack;
    private int freeCount;

    private readonly int[] lruPrev;
    private readonly int[] lruNext;
    private readonly int[] pinCount;
    private readonly bool[] allocated;
    private int lruHead = -1;
    private int lruTail = -1;

    public int TileSizeTexels => tileSizeTexels;
    public int TilesPerAxis => tilesPerAxis;
    public int TilesPerAtlas => tilesPerAtlas;
    public int AtlasCount => atlasCount;
    public int CapacityPages => capacityPages;

    public int FreeCount => freeCount;

    public LumonScenePhysicalPagePool(in LumonScenePhysicalPoolPlan plan)
    {
        if (plan.TileSizeTexels <= 0) throw new ArgumentOutOfRangeException(nameof(plan));
        if (plan.TilesPerAxis <= 0) throw new ArgumentOutOfRangeException(nameof(plan));
        if (plan.TilesPerAtlas <= 0) throw new ArgumentOutOfRangeException(nameof(plan));
        if (plan.AtlasCount <= 0) throw new ArgumentOutOfRangeException(nameof(plan));
        if (plan.CapacityPages <= 0) throw new ArgumentOutOfRangeException(nameof(plan));

        tileSizeTexels = plan.TileSizeTexels;
        tilesPerAxis = plan.TilesPerAxis;
        tilesPerAtlas = plan.TilesPerAtlas;
        atlasCount = plan.AtlasCount;
        capacityPages = plan.CapacityPages;

        freeStack = new int[capacityPages];
        lruPrev = new int[capacityPages];
        lruNext = new int[capacityPages];
        pinCount = new int[capacityPages];
        allocated = new bool[capacityPages];

        Array.Fill(lruPrev, -1);
        Array.Fill(lruNext, -1);

        // Fill free stack with indices [0..capacityPages-1].
        for (int i = 0; i < capacityPages; i++)
        {
            freeStack[i] = i;
        }

        freeCount = capacityPages;
    }

    public bool TryAllocate(out LumonScenePhysicalPage page)
    {
        if (freeCount <= 0)
        {
            page = default;
            return false;
        }

        int idx = freeStack[--freeCount];
        allocated[idx] = true;
        pinCount[idx] = 0;
        InsertMru(idx);

        uint id = (uint)(idx + 1);
        DecodePhysicalId(id, out ushort atlasIdx, out ushort x, out ushort y);
        page = new LumonScenePhysicalPage(id, atlasIdx, x, y);
        return true;
    }

    public bool TryGetEvictionCandidate(out uint physicalPageId)
    {
        int idx = lruTail;
        while (idx >= 0)
        {
            if (allocated[idx] && pinCount[idx] == 0)
            {
                physicalPageId = (uint)(idx + 1);
                return true;
            }

            idx = lruPrev[idx];
        }

        physicalPageId = 0;
        return false;
    }

    public void Touch(uint physicalPageId)
    {
        int idx = (int)physicalPageId - 1;
        if ((uint)idx >= (uint)capacityPages)
        {
            return;
        }

        if (!allocated[idx])
        {
            return;
        }

        MoveToMru(idx);
    }

    public void Pin(uint physicalPageId)
    {
        int idx = (int)physicalPageId - 1;
        if ((uint)idx >= (uint)capacityPages)
        {
            return;
        }

        if (!allocated[idx])
        {
            return;
        }

        pinCount[idx]++;
    }

    public void Unpin(uint physicalPageId)
    {
        int idx = (int)physicalPageId - 1;
        if ((uint)idx >= (uint)capacityPages)
        {
            return;
        }

        if (!allocated[idx])
        {
            return;
        }

        if (pinCount[idx] > 0)
        {
            pinCount[idx]--;
        }
    }

    public void Free(uint physicalPageId)
    {
        int idx = (int)physicalPageId - 1;
        if ((uint)idx >= (uint)capacityPages)
        {
            return;
        }

        if (!allocated[idx])
        {
            return;
        }

        allocated[idx] = false;
        pinCount[idx] = 0;
        RemoveFromLru(idx);
        freeStack[freeCount++] = idx;
    }

    public void DecodePhysicalId(uint physicalPageId, out ushort atlasIndex, out ushort tileX, out ushort tileY)
    {
        // physicalPageId is 1-based.
        int pageIndex = (int)physicalPageId - 1;

        int a = pageIndex / tilesPerAtlas;
        int local = pageIndex - (a * tilesPerAtlas);
        int y = local / tilesPerAxis;
        int x = local - (y * tilesPerAxis);

        atlasIndex = (ushort)a;
        tileX = (ushort)x;
        tileY = (ushort)y;
    }

    private void InsertMru(int idx)
    {
        if (lruHead < 0)
        {
            lruHead = idx;
            lruTail = idx;
            lruPrev[idx] = -1;
            lruNext[idx] = -1;
            return;
        }

        lruPrev[idx] = -1;
        lruNext[idx] = lruHead;
        lruPrev[lruHead] = idx;
        lruHead = idx;
    }

    private void MoveToMru(int idx)
    {
        if (idx == lruHead)
        {
            return;
        }

        RemoveFromLru(idx);
        InsertMru(idx);
    }

    private void RemoveFromLru(int idx)
    {
        int p = lruPrev[idx];
        int n = lruNext[idx];

        if (p >= 0)
        {
            lruNext[p] = n;
        }
        else if (lruHead == idx)
        {
            lruHead = n;
        }

        if (n >= 0)
        {
            lruPrev[n] = p;
        }
        else if (lruTail == idx)
        {
            lruTail = p;
        }

        lruPrev[idx] = -1;
        lruNext[idx] = -1;
    }
}

