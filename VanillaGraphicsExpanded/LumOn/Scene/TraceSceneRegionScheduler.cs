using System;
using System.Collections.Generic;

using VanillaGraphicsExpanded.Collections;
using VanillaGraphicsExpanded.WorldPartition;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Preserves tracing content priority and retry queues while the shared coordinator owns residency.</summary>
internal sealed partial class TraceSceneRegionScheduler : IWorldCellWorkSink, IPartitionResidencyBackend
{
    private const long MinRetryMs = 50;
    private const long MaxBackoffMs = 5000;
    private const long ChunkUnavailableBaseBackoffMs = 100;
    private const long NotSeenLoadedProbeMs = 500;
    private const int NearDequeueBurst = 8;

    private readonly PartitionCoordinator worldPartition;
    private readonly Dictionary<ulong, TraceSceneRegionCell> cellsByPacked = new();

    private readonly IndexedMaxHeap<ulong> nearEligible = new();
    private readonly IndexedMaxHeap<ulong> farEligible = new();

    private readonly Queue<ulong> dirtyQueue = new();
    private readonly HashSet<ulong> dirtySet = new();

    private readonly CooldownMinQueue cooldownQueue = new();
    private readonly Dictionary<ulong, long> cooldownUntilByKey = new();

    private VectorInt3 windowMin;
    private VectorInt3 windowMax;
    private bool hasWindow;

    private long lastNowTick;

    private int appliedCount;

    private int nearBurstCount;

    public int InFlightCount { get; private set; }

    public int EligibleNearCount => nearEligible.Count;

    public int EligibleFarCount => farEligible.Count;

    public int AppliedCount => appliedCount;

    public int SuppressedCount => cooldownUntilByKey.Count;

    public bool HasWindow => hasWindow;

    public long NowTick => lastNowTick;

    #region Domain scheduling API
    /// <summary>Attaches content scheduling to the shared rendering residency authority.</summary>
    public TraceSceneRegionScheduler(PartitionCoordinator worldPartition)
    {
        this.worldPartition = worldPartition ?? throw new ArgumentNullException(nameof(worldPartition));
    }

    /// <summary>Retires the registration before discarding domain queues and source-version observations.</summary>
    public void Reset()
    {
        if (instance != 0) worldPartition.Unregister(instance);
        instance = 0;

        cellsByPacked.Clear();
        nearEligible.Clear();
        farEligible.Clear();
        dirtyQueue.Clear();
        dirtySet.Clear();
        cooldownQueue.Clear();
        cooldownUntilByKey.Clear();

        windowMin = default;
        windowMax = default;
        hasWindow = false;

        lastNowTick = 0;
        nearBurstCount = 0;
        InFlightCount = 0;
        appliedCount = 0;
    }

    /// <summary>Replaces the source envelope without changing fixed region boundaries.</summary>
    public void SetWindow(in VectorInt3 min, in VectorInt3 max)
    {
        bool changed = !hasWindow || min != windowMin || max != windowMax;

        windowMin = min;
        windowMax = max;
        hasWindow = true;

        if (changed)
        {
            UpdateCoverage();
        }
    }

    /// <summary>Invalidates current publication authorization and refreshes domain priority for a wanted chunk.</summary>
    public void NotifyChunkDirty(ChunkKey chunkKey, int currentVersion, long nowTick, string? reason = null)
    {
        _ = reason;

        lastNowTick = Math.Max(lastNowTick, nowTick);

        if (!Contains(chunkKey)) return;
        TraceSceneRegionCell cell = GetOrCreateCell(chunkKey);

        if (currentVersion > cell.CurrentVersion)
        {
            cell.CurrentVersion = currentVersion;
            worldPartition.Dirty(PartitionKey(chunkKey));
        }

        MarkDirty(cell.ChunkKey.Packed);
    }

    /// <summary>Resets dependency polling delay after a source chunk becomes available.</summary>
    public void NotifyChunkSeenLoaded(ChunkKey chunkKey, long nowTick)
    {
        lastNowTick = Math.Max(lastNowTick, nowTick);
        if (!Contains(chunkKey)) return;
        TraceSceneRegionCell cell = GetOrCreateCell(chunkKey);
        cell.LastSeenLoadedTick = lastNowTick;

        // If we previously cooled down due to missing chunk, re-enable quickly.
        if (cell.MissingStreak > 0 || cell.NextEligibleTick > lastNowTick)
        {
            cell.MissingStreak = 0;
            cell.NextEligibleTick = 0;
            worldPartition.Dirty(PartitionKey(chunkKey));
        }

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

            if (cooldownUntilByKey.TryGetValue(packed, out long currentTick) && currentTick != tick)
            {
                continue;
            }

            cooldownUntilByKey.Remove(packed);

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

            break;
        }

        return refreshed;
    }

    public bool TryDequeueNextEligible(long nowTick, out ChunkKey key, out int priorityHint)
    {
        lastNowTick = nowTick;

        // Prefer near to ensure the near window cannot be starved, but never block when no near work exists.
        // (If nearEligible is empty, we must allow far immediately.)
        bool allowFar = nearBurstCount >= NearDequeueBurst;

        if (TryDequeueFromHeapsPreferNearAllowFar(out ulong packedKey, out bool fromNear, ref allowFar))
        {
            nearBurstCount = fromNear ? nearBurstCount + 1 : 0;
            key = new ChunkKey(packedKey);
            priorityHint = fromNear ? 1 : 0;
            return true;
        }

        key = default;
        priorityHint = 0;
        return false;
    }

    private bool TryDequeueFromHeapsPreferNearAllowFar(out ulong packedKey, out bool fromNear, ref bool allowFar)
    {
        // Try near first.
        if (TryPopValid(nearEligible, out packedKey))
        {
            fromNear = true;
            return true;
        }

        // If near drained to empty (or is empty), allow far regardless of burst gating.
        if (nearEligible.Count == 0)
        {
            allowFar = true;
        }

        if (allowFar && TryPopValid(farEligible, out packedKey))
        {
            fromNear = false;
            return true;
        }

        packedKey = 0;
        fromNear = false;
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
            if (!TryDequeueNextEligible(nowTick, out ChunkKey key, out _))
            {
                break;
            }

            dst[count] = key;
        }

        return count;
    }

    /// <summary>Obtains shared residency and work credit before dispatching the selected content update.</summary>
    public bool OnRequestIssued(ChunkKey key, int version, long nowTick)
    {
        lastNowTick = Math.Max(lastNowTick, nowTick);
        if (!Contains(key)) return false;
        TraceSceneRegionCell cell = GetOrCreateCell(key);

        if (version > cell.CurrentVersion)
        {
            cell.CurrentVersion = version;
        }

        if (cell.InFlightVersion != 0)
        {
            return false;
        }

        if (!worldPartition.TryBeginUpdate(PartitionKey(key), out PartitionRequest? request))
        {
            MarkDirty(key.Packed);
            return false;
        }
        cell.Request = request;
        cell.InFlightVersion = version;
        cell.LastAttemptTick = lastNowTick;
        InFlightCount++;

        RemoveFromHeaps(cell.ChunkKey.Packed);
        return true;
    }

    /// <summary>Acknowledges a failed domain operation using its immutable request identity.</summary>
    public void OnRequestCompleted(
        PartitionRequest request,
        ChunkKey key,
        ChunkWorkStatus status,
        int requestedVersion,
        long nowTick,
        ChunkWorkError error = ChunkWorkError.None)
    {
        if (status == ChunkWorkStatus.Success) throw new ArgumentException("Successful work must pass publication authorization.", nameof(status));
        worldPartition.FinishUpdate(request, status == ChunkWorkStatus.ChunkUnavailable);
        if (!cellsByPacked.TryGetValue(key.Packed, out TraceSceneRegionCell? cell) || cell.Request != request) return;
        cell.Request = null;
        CompleteDomainUpdate(cell, status, requestedVersion, nowTick, error);
    }

    /// <summary>Updates domain retry and priority metadata after the coordinator has accepted the outcome.</summary>
    private void CompleteDomainUpdate(TraceSceneRegionCell cell, ChunkWorkStatus status, int requestedVersion,
        long nowTick, ChunkWorkError error = ChunkWorkError.None)
    {
        lastNowTick = Math.Max(lastNowTick, nowTick);
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

                if (cell.AppliedVersion == 0)
                {
                    appliedCount++;
                }

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
                // Never retry faster than MinRetryMs to avoid per-frame churn.
                cell.NextEligibleTick = Math.Max(cell.NextEligibleTick, checked(lastNowTick + MinRetryMs));
                SetCooldown(cell.ChunkKey.Packed, cell.NextEligibleTick);
                MarkDirty(cell.ChunkKey.Packed);
                break;
        }
    }

    private void ApplyBackoff(TraceSceneRegionCell cell, bool aggressive)
    {
        cell.MissingStreak++;

        long delay = ComputeBackoffTicks(cell.MissingStreak, aggressive);
        delay = Math.Max(delay, MinRetryMs);
        cell.NextEligibleTick = checked(lastNowTick + delay);

        RemoveFromHeaps(cell.ChunkKey.Packed);
        SetCooldown(cell.ChunkKey.Packed, cell.NextEligibleTick);

        MarkDirty(cell.ChunkKey.Packed);
    }

    private static long ComputeBackoffTicks(int missingStreak, bool aggressive)
    {
        int s = Math.Clamp(missingStreak, 1, 30);

        long baseMs = aggressive ? ChunkUnavailableBaseBackoffMs : (ChunkUnavailableBaseBackoffMs / 2);
        long backoff = baseMs;
        int shift = Math.Min(s - 1, 10);
        backoff <<= shift;

        return Math.Min(backoff, MaxBackoffMs);
    }

    private void SetCooldown(ulong packedKey, long tick)
    {
        if (tick <= 0)
        {
            cooldownUntilByKey.Remove(packedKey);
            return;
        }

        cooldownUntilByKey[packedKey] = tick;
        cooldownQueue.Push(tick, packedKey);
    }

    void IWorldCellWorkSink.SetCooldown(WorldCellKey key, long tick)
        => SetCooldown(key.Packed, tick);

    void IWorldCellWorkSink.Remove(WorldCellKey key, WorldCellWorkQueue queue)
    {
        if (queue == WorldCellWorkQueue.EligibleNear)
        {
            nearEligible.Remove(key.Packed);
            return;
        }

        if (queue == WorldCellWorkQueue.EligibleFar)
        {
            farEligible.Remove(key.Packed);
            return;
        }
    }

    void IWorldCellWorkSink.Upsert(WorldCellKey key, WorldCellWorkQueue queue, float priority)
    {
        if (queue == WorldCellWorkQueue.EligibleNear)
        {
            nearEligible.Upsert(key.Packed, priority);
            farEligible.Remove(key.Packed);
            return;
        }

        if (queue == WorldCellWorkQueue.EligibleFar)
        {
            farEligible.Upsert(key.Packed, priority);
            nearEligible.Remove(key.Packed);
            return;
        }
    }

    public bool TryGetTopK(int k, long nowTick, out string line)
    {
        line = string.Empty;

        if (k <= 0)
        {
            return false;
        }

        lastNowTick = Math.Max(lastNowTick, nowTick);

        Span<ulong> near = stackalloc ulong[Math.Min(k, nearEligible.Count)];
        Span<ulong> far = stackalloc ulong[Math.Min(k, farEligible.Count)];

        int nearCount = nearEligible.CopyTopKeys(near);
        int farCount = farEligible.CopyTopKeys(far);

        if (nearCount <= 0 && farCount <= 0)
        {
            return false;
        }

        Span<ulong> merged = stackalloc ulong[Math.Min(k * 2, Math.Max(1, nearCount + farCount))];
        int mergedCount = 0;
        for (int i = 0; i < nearCount && mergedCount < merged.Length; i++) merged[mergedCount++] = near[i];
        for (int i = 0; i < farCount && mergedCount < merged.Length; i++) merged[mergedCount++] = far[i];

        Span<ulong> top = stackalloc ulong[Math.Min(k, mergedCount)];
        Span<float> topPri = stackalloc float[top.Length];
        int topCount = 0;

        for (int i = 0; i < mergedCount; i++)
        {
            ulong pk = merged[i];
            if (!cellsByPacked.TryGetValue(pk, out TraceSceneRegionCell? cell)) continue;
            if (!IsInWindow(cell.RegionCoord)) continue;
            if (cell.InFlightVersion != 0) continue;
            if (cell.NextEligibleTick > lastNowTick) continue;

            float p = cell.Priority;
            if (float.IsNegativeInfinity(p) || float.IsNaN(p)) continue;

            int insert = topCount;
            while (insert > 0 && p > topPri[insert - 1])
            {
                if (insert < top.Length)
                {
                    top[insert] = top[insert - 1];
                    topPri[insert] = topPri[insert - 1];
                }

                insert--;
            }

            if (insert < top.Length)
            {
                if (topCount < top.Length) topCount++;
                top[insert] = pk;
                topPri[insert] = p;
            }
        }

        if (topCount <= 0)
        {
            return false;
        }

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < topCount; i++)
        {
            if (!cellsByPacked.TryGetValue(top[i], out TraceSceneRegionCell? cell)) continue;
            if (sb.Length > 0) sb.Append(" | ");
            sb.Append(cell.RegionCoord.X).Append(',').Append(cell.RegionCoord.Y).Append(',').Append(cell.RegionCoord.Z);
            sb.Append(" p:").Append(cell.Priority.ToString("0.0"));
            sb.Append(" r:").Append(FormatReasonAbbrev(cell.LastPriorityReasons));
        }

        line = sb.ToString();
        return line.Length > 0;
    }

    public string DumpState(int topN, long nowTick)
    {
        lastNowTick = Math.Max(lastNowTick, nowTick);
        topN = Math.Clamp(topN, 1, 256);

        var sb = new System.Text.StringBuilder();
        sb.Append("TraceSceneRegionScheduler: ");
        sb.Append("eligible=").Append(nearEligible.Count + farEligible.Count);
        sb.Append(" (near=").Append(nearEligible.Count).Append(", far=").Append(farEligible.Count).Append(')');
        sb.Append(" suppressed=").Append(SuppressedCount);
        sb.Append(" inflight=").Append(InFlightCount);
        sb.Append(" applied=").Append(AppliedCount);
        sb.Append(" nowMs=").Append(lastNowTick);

        if (TryGetTopK(Math.Min(8, topN), nowTick, out string topLine))
        {
            sb.Append("\nTop: ").Append(topLine);
        }

        int scanned = 0;
        List<TraceSceneRegionCell> best = new(capacity: Math.Min(topN, 64));
        foreach (TraceSceneRegionCell cell in cellsByPacked.Values)
        {
            scanned++;
            if (!IsInWindow(cell.RegionCoord)) continue;
            if (cell.InFlightVersion != 0) continue;

            float p = cell.Priority;
            if (float.IsNegativeInfinity(p) || float.IsNaN(p)) continue;

            best.Add(cell);
        }

        best.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        int take = Math.Min(topN, best.Count);

        sb.Append("\nEntries (top ").Append(take).Append(" of ").Append(best.Count).Append(", scanned ").Append(scanned).Append("):");
        for (int i = 0; i < take; i++)
        {
            TraceSceneRegionCell c = best[i];
            sb.Append("\n  ");
            sb.Append(c.RegionCoord.X).Append(',').Append(c.RegionCoord.Y).Append(',').Append(c.RegionCoord.Z);
            sb.Append(" p=").Append(c.Priority.ToString("0.00"));
            sb.Append(" r=").Append(FormatReasonAbbrev(c.LastPriorityReasons));
            sb.Append(" v=").Append(c.AppliedVersion).Append("->").Append(c.CurrentVersion);
            if (c.MissingStreak > 0) sb.Append(" miss=").Append(c.MissingStreak);
            if (c.NextEligibleTick > lastNowTick) sb.Append(" cdUntil=").Append(c.NextEligibleTick);
        }

        return sb.ToString();
    }

    private static string FormatReasonAbbrev(TraceSceneRegionPriorityReason reasons)
    {
        if (reasons == TraceSceneRegionPriorityReason.None) return "-";

        var sb = new System.Text.StringBuilder();

        void Add(char c)
        {
            if (sb.Length > 0) sb.Append('+');
            sb.Append(c);
        }

        if ((reasons & TraceSceneRegionPriorityReason.Distance) != 0) Add('D');
        if ((reasons & TraceSceneRegionPriorityReason.Stale) != 0) Add('S');
        if ((reasons & TraceSceneRegionPriorityReason.NeverApplied) != 0) Add('N');
        if ((reasons & TraceSceneRegionPriorityReason.SeenLoadedRecently) != 0) Add('L');
        if ((reasons & TraceSceneRegionPriorityReason.MissingPenalty) != 0) Add('M');
        if ((reasons & TraceSceneRegionPriorityReason.CooldownSuppressed) != 0) Add('C');

        return sb.ToString();
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

            if (cell.LastSeenLoadedTick <= 0)
            {
                continue;
            }

            if (cell.InFlightVersion != 0)
            {
                continue;
            }

            if (cell.NextEligibleTick > lastNowTick)
            {
                SetCooldown(packedKey, cell.NextEligibleTick);
                LumonSceneTraceSceneMetrics.OnCooldownSkip();
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
            cell.DequeueSelf(this);
            return;
        }

        if (!worldPartition.TryGetCell(PartitionKey(cell.ChunkKey), out PartitionCellInfo info))
        {
            cell.DequeueSelf(this);
            return;
        }
        cell.DesiredState = (WorldCellDesiredState)info.Desired;

        // If a region has never been observed loaded, it won't be eligible to request a snapshot.
        // However, relying solely on ChunkDirty can miss initial/quiet chunk loads; use an optional
        // loadedness probe to "discover" loaded chunks and periodically re-check.
        if (cell.LastSeenLoadedTick <= 0)
        {
            bool isLoaded = context.IsCellLikelyLoaded is not null && context.IsCellLikelyLoaded(cell.Key);
            if (isLoaded)
            {
                cell.LastSeenLoadedTick = context.NowTick;

                // If we previously cooled down due to missing chunk, re-enable quickly.
                if (cell.MissingStreak > 0 || cell.NextEligibleTick > context.NowTick)
                {
                    cell.MissingStreak = 0;
                    cell.NextEligibleTick = 0;
                }
            }
            else
            {
                // Schedule a future probe so the cell becomes eligible once the chunk loads,
                // even if no ChunkDirty is observed for it.
                cell.NextEligibleTick = Math.Max(cell.NextEligibleTick, checked(context.NowTick + NotSeenLoadedProbeMs));
            }
        }

        // Cells own queue placement logic; scheduler provides the sink implementation.
        cell.EnqueueSelf(this, in context);
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

        WorldCellKey key = WorldCellKey.FromTraceSceneRegion(chunkKey.Packed);
        TraceSceneRegionCell cell = new(chunkKey);
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

    // Seeding/scanning logic removed in favor of explicit window-delta enqueueing by the clipmap renderer.

    #endregion

    #region Retry ordering
    /// <summary>Orders domain millisecond cooldowns independently of coordinator lifecycle retry ticks.</summary>
    private sealed class CooldownMinQueue
    {
        private readonly List<Entry> heap = new();

        public void Clear() => heap.Clear();

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
    #endregion
}
