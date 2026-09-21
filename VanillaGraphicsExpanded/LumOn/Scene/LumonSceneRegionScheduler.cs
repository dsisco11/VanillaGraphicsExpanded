using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;

using VanillaGraphicsExpanded.Collections;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>
/// Domain capture and relight queues for coordinator-owned scene residency.
/// Owns the queues; cells enqueue themselves via <see cref="IWorldCellWorkSink"/>.
/// </summary>
internal sealed partial class LumonSceneRegionScheduler : IWorldCellWorkSink, IPartitionResidencyBackend
{
    private readonly PartitionCoordinator worldPartition;
    private readonly Action<LumonSceneChunkCoord>? retireSlot;
    private readonly Dictionary<WorldCellKey, LumonSceneRegionCell> cells = new();


    // Active update work queues (future: may be split further).
    private readonly IndexedMaxHeap<WorldCellKey> capture = new();
    private readonly IndexedMaxHeap<WorldCellKey> relight = new();

    private long nowTick;

    public long NowTick => nowTick;

    public int CellCount => cells.Count;


    public int CaptureCount => capture.Count;

    public int RelightCount => relight.Count;

    #region Domain queue API
    /// <summary>Shares residency authority and forwards acknowledged retirement to the slot backend.</summary>
    public LumonSceneRegionScheduler(PartitionCoordinator worldPartition, Action<LumonSceneChunkCoord>? retireSlot = null)
    {
        this.worldPartition = worldPartition ?? throw new ArgumentNullException(nameof(worldPartition));
        this.retireSlot = retireSlot;
    }

    public bool TryGetCell(WorldCellKey key, out LumonSceneRegionCell cell)
        => cells.TryGetValue(key, out cell!);

    public int CopyDebugSnapshots(Span<LumonSceneRegionCellDebugSnapshot> dst)
    {
        if (dst.Length <= 0 || cells.Count <= 0)
        {
            return 0;
        }

        int written = 0;
        foreach (LumonSceneRegionCell cell in cells.Values)
        {
            if ((uint)written >= (uint)dst.Length)
            {
                break;
            }

            dst[written++] = new LumonSceneRegionCellDebugSnapshot(
                ChunkCoord: cell.ChunkCoordInt3,
                DesiredState: cell.DesiredState,
                ActualState: cell.ActualState);
        }

        return written;
    }

    public void Reset(long nowTick)
    {
        this.nowTick = nowTick;

        ReleasePartitions();
        cells.Clear();
        capture.Clear();
        relight.Clear();
    }

    public LumonSceneRegionCell GetOrCreate(WorldCellKind kind, in LumonSceneChunkCoord coord)
    {
        LumonSceneChunkCoord coordCopy = coord;
        var key = kind == WorldCellKind.LumonSceneNear
            ? WorldCellKey.FromLumonSceneNear(coordCopy.ToKey())
            : WorldCellKey.FromLumonSceneFar(coordCopy.ToKey());

        if (cells.TryGetValue(key, out LumonSceneRegionCell? existing))
        {
            return existing;
        }

        LumonSceneRegionCell created = new(kind, in coordCopy);
        cells.Add(key, created);
        return created;
    }


    public bool TryPopCapture(out WorldCellKey key)
        => capture.TryPopMax(out key);

    public int CopyTopCaptureKeys(Span<WorldCellKey> dst)
        => capture.CopyTopKeys(dst);

    public bool TryPopRelight(out WorldCellKey key)
        => relight.TryPopMax(out key);

    public int CopyTopRelightKeys(Span<WorldCellKey> dst)
        => relight.CopyTopKeys(dst);

    public void SetNowTick(long nowTick)
        => this.nowTick = nowTick;

    public void Upsert(WorldCellKey key, WorldCellWorkQueue queue, float priority)
    {
        switch (queue)
        {
            case WorldCellWorkQueue.Capture:
                capture.Upsert(key, priority);
                break;

            case WorldCellWorkQueue.Relight:
                relight.Upsert(key, priority);
                break;

            // Not used by LumonScene yet; safe no-op.
            case WorldCellWorkQueue.EligibleNear:
            case WorldCellWorkQueue.EligibleFar:
            default:
                break;
        }
    }

    public void Remove(WorldCellKey key, WorldCellWorkQueue queue)
    {
        switch (queue)
        {
            case WorldCellWorkQueue.Capture:
                capture.Remove(key);
                break;

            case WorldCellWorkQueue.Relight:
                relight.Remove(key);
                break;

            // Not used by LumonScene yet; safe no-op.
            case WorldCellWorkQueue.EligibleNear:
            case WorldCellWorkQueue.EligibleFar:
            default:
                break;
        }
    }

    public void SetCooldown(WorldCellKey key, long tick)
    {
        if (!cells.TryGetValue(key, out LumonSceneRegionCell? cell))
        {
            return;
        }

        cell.NextEligibleTick = tick;
    }

    public string DumpState(int topN)
    {
        topN = Math.Max(0, topN);

        var sb = new StringBuilder(capacity: 2048);
        sb.Append("LS scheduler: ");
        sb.Append("cells=").Append(CellCount)

            .Append(" qC=").Append(CaptureCount)
            .Append(" qR=").Append(RelightCount)
            .Append(" now=").Append(nowTick);

        AppendQueueDump(sb, "C", capture, topN);
        AppendQueueDump(sb, "R", relight, topN);

        return sb.ToString();
    }

    #endregion

    #region Queue diagnostics
    /// <summary>Formats a bounded priority-ordered sample without exposing mutable queue storage.</summary>
    private void AppendQueueDump(StringBuilder sb, string tag, IndexedMaxHeap<WorldCellKey> heap, int topN)
    {
        if (topN <= 0 || heap.Count <= 0)
        {
            return;
        }

        int want = Math.Min(topN, heap.Count);
        WorldCellKey[] arr = ArrayPool<WorldCellKey>.Shared.Rent(want);
        try
        {
            int got = heap.CopyTopKeys(arr.AsSpan(0, want));
            if (got <= 0)
            {
                return;
            }

            sb.Append(" | ").Append(tag).Append(" top ");
            sb.Append(got).Append(": ");

            for (int i = 0; i < got; i++)
            {
                WorldCellKey key = arr[i];
                _ = heap.TryGetPriority(key, out float pri);

                if (!cells.TryGetValue(key, out LumonSceneRegionCell? cell))
                {
                    sb.Append("[").Append(i).Append("] ").Append(key.Kind).Append(":").Append(key.Packed).Append(" pri=").Append(pri.ToString("0.##"));
                }
                else
                {
                    sb.Append("[").Append(i).Append("] ")
                        .Append(cell.Kind)
                        .Append(" c=").Append(cell.ChunkCoordInt3.X).Append(",").Append(cell.ChunkCoordInt3.Y).Append(",").Append(cell.ChunkCoordInt3.Z)
                        .Append(" d=").Append(cell.DesiredState)
                        .Append(" a=").Append(cell.ActualState)
                        .Append(" slot=").Append(cell.HasAssignedSlot ? cell.ChunkSlot : uint.MaxValue)
                        .Append(" gen=").Append(cell.SlotGeneration)
                        .Append(" nc=").Append(cell.NeedsCapturePages)
                        .Append(" nr=").Append(cell.NeedsRelightPages)
                        .Append(" heat=").Append(cell.LastHeatRequestCount)
                        .Append(" cd=").Append(cell.NextEligibleTick)
                        .Append(" pri=").Append(pri.ToString("0.##"));
                }

                if (i < got - 1)
                {
                    sb.Append(" ; ");
                }
            }
        }
        finally
        {
            ArrayPool<WorldCellKey>.Shared.Return(arr, clearArray: false);
        }
    }
    #endregion
}
