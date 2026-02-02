using System;
using System.Collections.Generic;

using VanillaGraphicsExpanded.Collections;
using VanillaGraphicsExpanded.LumOn.WorldCells;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;

namespace VanillaGraphicsExpanded.LumOn.Scene;

internal sealed class TraceSceneRegionScheduler
{
    private const int NearDequeueBurst = 8;
    private const int NearRadiusRegions = 4;

    private readonly Dictionary<ulong, TraceSceneRegionCell> cellsByPacked = new();

    private readonly IndexedMaxHeap<ulong> nearEligible = new();
    private readonly IndexedMaxHeap<ulong> farEligible = new();

    private readonly Queue<ulong> dirtyQueue = new();
    private readonly HashSet<ulong> dirtySet = new();

    private readonly CooldownMinQueue cooldownQueue = new();

    private VectorInt3 windowMin;
    private VectorInt3 windowMax;
    private bool hasWindow;

    private long seedCursor;
    private long seedTotal;
    private bool seedActive;

    private long lastNowTick;

    private int nearBurstCount;

    public int InFlightCount { get; private set; }

    public bool HasWindow => hasWindow;

    public void SetWindow(in VectorInt3 min, in VectorInt3 max)
    {
        VectorInt3 clampedMin = min;
        VectorInt3 clampedMax = max;

        // VintageStory chunks do not exist below 0.
        if (clampedMin.Y < 0) clampedMin = new VectorInt3(clampedMin.X, 0, clampedMin.Z);
        if (clampedMax.Y < 0) clampedMax = new VectorInt3(clampedMax.X, -1, clampedMax.Z);

        if (clampedMax.Y < clampedMin.Y)
        {
            hasWindow = false;
            seedActive = false;
            seedCursor = 0;
            seedTotal = 0;

            nearEligible.Clear();
            farEligible.Clear();

            TrimCellsToWindow();
            return;
        }

        bool changed = !hasWindow || clampedMin != windowMin || clampedMax != windowMax;

        windowMin = clampedMin;
        windowMax = clampedMax;
        hasWindow = true;

        if (changed)
        {
            ResetSeed();
            TrimCellsToWindow();
        }
    }

    public void NotifyChunkDirty(ChunkKey chunkKey, string? reason = null)
    {
        _ = reason;

        TraceSceneRegionCell cell = GetOrCreateCell(chunkKey);

        // Ensure monotonically increasing change version.
        int nextVersion = cell.CurrentVersion + 1;
        if (nextVersion <= 0) nextVersion = 1;
        cell.CurrentVersion = nextVersion;

        MarkDirty(cell.ChunkKey.Packed);
    }

    public void NotifyChunkSeenLoaded(ChunkKey chunkKey)
    {
        TraceSceneRegionCell cell = GetOrCreateCell(chunkKey);
        cell.LastSeenLoadedTick = lastNowTick;
        MarkDirty(cell.ChunkKey.Packed);
    }

    public int RefreshPriorities(in WorldCellPriorityContext context, int budget)
    {
        if (budget <= 0)
        {
            return 0;
        }

        lastNowTick = context.NowTick;

        // Promote cooldown-expired entries back into the refresh queue.
        while (cooldownQueue.TryPeek(out long tick, out ulong packed) && tick <= lastNowTick)
        {
            _ = cooldownQueue.TryPop(out _, out packed);

            if (!cellsByPacked.TryGetValue(packed, out TraceSceneRegionCell? cell))
            {
                continue;
            }

            if (cell.InFlightVersion != 0)
            {
                continue;
            }

            if (cell.NextEligibleTick <= lastNowTick)
            {
                MarkDirty(packed);
            }
        }

        var schedulerContext = new WorldCellPriorityContext(
            CameraBlockPos: context.CameraBlockPos,
            AnchorBlockPos: context.AnchorBlockPos,
            HasAnchor: context.HasAnchor,
            WindowMinRegion: windowMin,
            WindowMaxRegion: windowMax,
            HasWindow: hasWindow,
            NowTick: context.NowTick,
            IsCellLikelyLoaded: context.IsCellLikelyLoaded);

        int refreshed = 0;
        while (refreshed < budget)
        {
            if (TryDequeueDirty(out ulong packedKey))
            {
                RefreshCell(packedKey, in schedulerContext);
                refreshed++;
                continue;
            }

            if (TryGetNextSeedRegion(out VectorInt3 regionCoord))
            {
                TraceSceneRegionCell cell = GetOrCreateCell(regionCoord);
                RefreshCell(cell.ChunkKey.Packed, in schedulerContext);
                refreshed++;
                continue;
            }

            break;
        }

        return refreshed;
    }

    public bool TryDequeueNextEligible(long nowTick, out ChunkKey key)
    {
        lastNowTick = nowTick;

        // Prefer near to ensure near window cannot be starved.
        bool allowFar = nearBurstCount >= NearDequeueBurst;

        if (TryDequeueFromHeaps(preferNear: true, allowFar: allowFar, out ulong packedKey, out bool fromNear))
        {
            nearBurstCount = fromNear ? nearBurstCount + 1 : 0;
            key = new ChunkKey(packedKey);
            return true;
        }

        key = default;
        return false;
    }

    public int DequeueBatch(long nowTick, Span<ChunkKey> dst)
    {
        if (dst.Length <= 0)
        {
            return 0;
        }

        int count = 0;
        for (; count < dst.Length; count++)
        {
            if (!TryDequeueNextEligible(nowTick, out ChunkKey key))
            {
                break;
            }

            dst[count] = key;
        }

        return count;
    }

    public void OnRequestIssued(ChunkKey key, int version)
    {
        TraceSceneRegionCell cell = GetOrCreateCell(key);

        if (cell.InFlightVersion != 0)
        {
            return;
        }

        cell.InFlightVersion = version;
        cell.LastAttemptTick = lastNowTick;
        InFlightCount++;

        RemoveFromHeaps(cell.ChunkKey.Packed);
    }

    public void OnRequestCompleted(ChunkKey key, ChunkWorkStatus status, int requestedVersion, ChunkWorkError error = ChunkWorkError.None)
    {
        TraceSceneRegionCell cell = GetOrCreateCell(key);

        if (cell.InFlightVersion != 0)
        {
            cell.InFlightVersion = 0;
            if (InFlightCount > 0)
            {
                InFlightCount--;
            }
        }

        cell.LastAttemptTick = lastNowTick;

        switch (status)
        {
            case ChunkWorkStatus.Success:
                cell.MissingStreak = 0;
                cell.NextEligibleTick = 0;
                cell.AppliedVersion = requestedVersion;

                // If it changed while in-flight, it is immediately eligible again.
                if (cell.CurrentVersion != cell.AppliedVersion)
                {
                    MarkDirty(cell.ChunkKey.Packed);
                }
                else
                {
                    RemoveFromHeaps(cell.ChunkKey.Packed);
                }

                break;

            case ChunkWorkStatus.ChunkUnavailable:
                ApplyBackoff(cell, aggressive: true);
                break;

            case ChunkWorkStatus.Failed:
                // Snapshot failures behave similarly to "missing" until the chunk arrives.
                ApplyBackoff(cell, aggressive: error == ChunkWorkError.SnapshotFailed);
                break;

            case ChunkWorkStatus.Canceled:
            case ChunkWorkStatus.Superseded:
            default:
                // Re-evaluate; caller may have re-issued or window may have changed.
                MarkDirty(cell.ChunkKey.Packed);
                break;
        }
    }

    private void ApplyBackoff(TraceSceneRegionCell cell, bool aggressive)
    {
        cell.MissingStreak++;

        long delay = ComputeBackoffTicks(cell.MissingStreak, aggressive);
        cell.NextEligibleTick = checked(lastNowTick + delay);

        RemoveFromHeaps(cell.ChunkKey.Packed);
        cooldownQueue.Push(cell.NextEligibleTick, cell.ChunkKey.Packed);

        MarkDirty(cell.ChunkKey.Packed);
    }

    private static long ComputeBackoffTicks(int missingStreak, bool aggressive)
    {
        int s = Math.Clamp(missingStreak, 1, 20);

        long baseTicks = aggressive ? 30 : 10;
        long maxTicks = aggressive ? 900 : 300;

        int shift = Math.Min(s - 1, 6);
        long backoff = baseTicks << shift;

        return Math.Min(backoff, maxTicks);
    }

    private bool TryDequeueFromHeaps(bool preferNear, bool allowFar, out ulong packedKey, out bool fromNear)
    {
        // Opportunistically skip cooldown-suppressed cells, ensuring they don't block other work.
        while (true)
        {
            if (preferNear && TryPopValid(nearEligible, out packedKey))
            {
                fromNear = true;
                return true;
            }

            if (allowFar && TryPopValid(farEligible, out packedKey))
            {
                nearBurstCount = 0;
                fromNear = false;
                return true;
            }

            // If preferNear failed, allow trying the other heap even when allowFar is false.
            if (!preferNear && TryPopValid(farEligible, out packedKey))
            {
                fromNear = false;
                return true;
            }

            if (!preferNear && TryPopValid(nearEligible, out packedKey))
            {
                fromNear = true;
                return true;
            }

            break;
        }

        packedKey = 0;
        fromNear = false;
        return false;
    }

    private bool TryPopValid(IndexedMaxHeap<ulong> heap, out ulong packedKey)
    {
        while (heap.TryPopMax(out packedKey))
        {
            if (!cellsByPacked.TryGetValue(packedKey, out TraceSceneRegionCell? cell))
            {
                continue;
            }

            if (!IsInWindow(cell.RegionCoord))
            {
                continue;
            }

            if (cell.InFlightVersion != 0)
            {
                continue;
            }

            if (cell.NextEligibleTick > lastNowTick)
            {
                cooldownQueue.Push(cell.NextEligibleTick, packedKey);
                continue;
            }

            return true;
        }

        packedKey = 0;
        return false;
    }

    private void RefreshCell(ulong packedKey, in WorldCellPriorityContext context)
    {
        if (!cellsByPacked.TryGetValue(packedKey, out TraceSceneRegionCell? cell))
        {
            cell = GetOrCreateCell(new ChunkKey(packedKey));
        }

        if (!IsInWindow(cell.RegionCoord) || cell.InFlightVersion != 0)
        {
            RemoveFromHeaps(cell.ChunkKey.Packed);
            return;
        }

        float priority = cell.CalculatePriority(in context);
        cell.Priority = priority;

        if (float.IsNegativeInfinity(priority) || float.IsNaN(priority))
        {
            RemoveFromHeaps(cell.ChunkKey.Packed);

            if (cell.NextEligibleTick > lastNowTick)
            {
                cooldownQueue.Push(cell.NextEligibleTick, cell.ChunkKey.Packed);
            }

            return;
        }

        bool isNear = IsNear(cell, in context);
        if (isNear)
        {
            nearEligible.Upsert(cell.ChunkKey.Packed, priority);
            farEligible.Remove(cell.ChunkKey.Packed);
        }
        else
        {
            farEligible.Upsert(cell.ChunkKey.Packed, priority);
            nearEligible.Remove(cell.ChunkKey.Packed);
        }
    }

    private static bool IsNear(TraceSceneRegionCell cell, in WorldCellPriorityContext context)
    {
        VectorInt3 anchorBlock = context.HasAnchor ? context.AnchorBlockPos : context.CameraBlockPos;
        VectorInt3 anchorRegion = LumonSceneTraceSceneClipmapMath.WorldCellToRegionCoord(anchorBlock);

        int dx = cell.RegionCoord.X - anchorRegion.X;
        int dy = cell.RegionCoord.Y - anchorRegion.Y;
        int dz = cell.RegionCoord.Z - anchorRegion.Z;

        long dist2 = (long)dx * dx + (long)dy * dy + (long)dz * dz;
        return dist2 <= (long)NearRadiusRegions * NearRadiusRegions;
    }

    private void MarkDirty(ulong packedKey)
    {
        if (dirtySet.Add(packedKey))
        {
            dirtyQueue.Enqueue(packedKey);
        }
    }

    private bool TryDequeueDirty(out ulong packedKey)
    {
        while (dirtyQueue.Count > 0)
        {
            packedKey = dirtyQueue.Dequeue();
            if (dirtySet.Remove(packedKey))
            {
                return true;
            }
        }

        packedKey = 0;
        return false;
    }

    private TraceSceneRegionCell GetOrCreateCell(ChunkKey chunkKey)
    {
        ulong packed = chunkKey.Packed;

        if (cellsByPacked.TryGetValue(packed, out TraceSceneRegionCell? existing))
        {
            return existing;
        }

        var cell = new TraceSceneRegionCell(chunkKey);
        cellsByPacked[packed] = cell;
        return cell;
    }

    private TraceSceneRegionCell GetOrCreateCell(VectorInt3 regionCoord)
        => GetOrCreateCell(ChunkKey.FromChunkCoords(regionCoord.X, regionCoord.Y, regionCoord.Z));

    private void RemoveFromHeaps(ulong packedKey)
    {
        nearEligible.Remove(packedKey);
        farEligible.Remove(packedKey);
    }

    private bool IsInWindow(in VectorInt3 regionCoord)
    {
        if (!hasWindow)
        {
            return false;
        }

        return regionCoord.X >= windowMin.X
               && regionCoord.Y >= windowMin.Y
               && regionCoord.Z >= windowMin.Z
               && regionCoord.X <= windowMax.X
               && regionCoord.Y <= windowMax.Y
               && regionCoord.Z <= windowMax.Z;
    }

    private void ResetSeed()
    {
        if (!hasWindow)
        {
            seedCursor = 0;
            seedTotal = 0;
            seedActive = false;
            return;
        }

        long sizeX = (long)windowMax.X - windowMin.X + 1L;
        long sizeY = (long)windowMax.Y - windowMin.Y + 1L;
        long sizeZ = (long)windowMax.Z - windowMin.Z + 1L;

        if (sizeX <= 0 || sizeY <= 0 || sizeZ <= 0)
        {
            seedCursor = 0;
            seedTotal = 0;
            seedActive = false;
            return;
        }

        seedCursor = 0;
        seedTotal = checked(sizeX * checked(sizeY * sizeZ));
        seedActive = seedTotal > 0;
    }

    private bool TryGetNextSeedRegion(out VectorInt3 regionCoord)
    {
        if (!seedActive || seedCursor >= seedTotal)
        {
            regionCoord = default;
            seedActive = false;
            return false;
        }

        long sizeX = (long)windowMax.X - windowMin.X + 1L;
        long sizeY = (long)windowMax.Y - windowMin.Y + 1L;

        long i = seedCursor++;

        long z = i / (sizeX * sizeY);
        long rem = i - (z * sizeX * sizeY);
        long y = rem / sizeX;
        long x = rem - (y * sizeX);

        regionCoord = new VectorInt3(
            checked(windowMin.X + (int)x),
            checked(windowMin.Y + (int)y),
            checked(windowMin.Z + (int)z));

        return true;
    }

    private void TrimCellsToWindow()
    {
        if (!hasWindow)
        {
            // Keep in-flight entries, but remove all eligibility.
            foreach ((ulong packed, TraceSceneRegionCell cell) in cellsByPacked)
            {
                if (cell.InFlightVersion == 0)
                {
                    RemoveFromHeaps(packed);
                }
            }

            return;
        }

        List<ulong>? toRemove = null;

        foreach ((ulong packed, TraceSceneRegionCell cell) in cellsByPacked)
        {
            if (cell.InFlightVersion != 0)
            {
                continue;
            }

            if (!IsInWindow(cell.RegionCoord))
            {
                toRemove ??= new List<ulong>();
                toRemove.Add(packed);
            }
        }

        if (toRemove is null)
        {
            return;
        }

        foreach (ulong packed in toRemove)
        {
            RemoveFromHeaps(packed);
            cellsByPacked.Remove(packed);
            dirtySet.Remove(packed);
        }
    }

    private sealed class CooldownMinQueue
    {
        private readonly List<Entry> heap = new();

        public void Push(long tick, ulong packedKey)
        {
            heap.Add(new Entry(tick, packedKey));
            HeapifyUp(heap.Count - 1);
        }

        public bool TryPeek(out long tick, out ulong packedKey)
        {
            if (heap.Count <= 0)
            {
                tick = 0;
                packedKey = 0;
                return false;
            }

            tick = heap[0].Tick;
            packedKey = heap[0].PackedKey;
            return true;
        }

        public bool TryPop(out long tick, out ulong packedKey)
        {
            if (heap.Count <= 0)
            {
                tick = 0;
                packedKey = 0;
                return false;
            }

            Entry top = heap[0];
            tick = top.Tick;
            packedKey = top.PackedKey;

            int last = heap.Count - 1;
            heap[0] = heap[last];
            heap.RemoveAt(last);

            if (heap.Count > 0)
            {
                HeapifyDown(0);
            }

            return true;
        }

        private void HeapifyUp(int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) >> 1;
                if (heap[index].Tick >= heap[parent].Tick)
                {
                    return;
                }

                (heap[parent], heap[index]) = (heap[index], heap[parent]);
                index = parent;
            }
        }

        private void HeapifyDown(int index)
        {
            while (true)
            {
                int left = (index << 1) + 1;
                if (left >= heap.Count)
                {
                    return;
                }

                int right = left + 1;
                int smallest = left;

                if (right < heap.Count && heap[right].Tick < heap[left].Tick)
                {
                    smallest = right;
                }

                if (heap[smallest].Tick >= heap[index].Tick)
                {
                    return;
                }

                (heap[smallest], heap[index]) = (heap[index], heap[smallest]);
                index = smallest;
            }
        }

        private readonly record struct Entry(long Tick, ulong PackedKey);
    }
}
