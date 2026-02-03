using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;

using VanillaGraphicsExpanded.Collections;
using VanillaGraphicsExpanded.LumOn.WorldCells;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>
/// Phase 9.1: minimal LumonScene region scheduler scaffold.
/// Owns the queues; cells enqueue themselves via <see cref="IWorldCellWorkSink"/>.
/// </summary>
internal sealed class LumonSceneRegionScheduler : IWorldCellWorkSink
{
    private readonly Dictionary<WorldCellKey, LumonSceneRegionCell> cells = new();

    // Cells that need lifecycle transitions (Unloaded↔Loaded↔Active).
    private readonly IndexedMaxHeap<WorldCellKey> transitions = new();

    // Active update work queues (future: may be split further).
    private readonly IndexedMaxHeap<WorldCellKey> capture = new();
    private readonly IndexedMaxHeap<WorldCellKey> relight = new();

    private long nowTick;

    public long NowTick => nowTick;

    public int CellCount => cells.Count;

    public int TransitionCount => transitions.Count;

    public int CaptureCount => capture.Count;

    public int RelightCount => relight.Count;

    public bool TryGetCell(WorldCellKey key, out LumonSceneRegionCell cell)
        => cells.TryGetValue(key, out cell!);

    public void Reset(long nowTick)
    {
        this.nowTick = nowTick;
        cells.Clear();
        transitions.Clear();
        capture.Clear();
        relight.Clear();
    }

    public void PruneToKeys(HashSet<WorldCellKey> keep)
    {
        if (keep is null) throw new ArgumentNullException(nameof(keep));
        if (cells.Count <= 0) return;

        List<WorldCellKey>? toRemove = null;
        foreach (WorldCellKey key in cells.Keys)
        {
            if (!keep.Contains(key))
            {
                toRemove ??= new List<WorldCellKey>();
                toRemove.Add(key);
            }
        }

        if (toRemove is null)
        {
            return;
        }

        foreach (WorldCellKey key in toRemove)
        {
            cells.Remove(key);
            transitions.Remove(key);
            capture.Remove(key);
            relight.Remove(key);
        }
    }

    public LumonSceneRegionCell GetOrCreate(WorldCellKind kind, in LumonSceneChunkCoord coord)
    {
        var key = kind == WorldCellKind.LumonSceneNear
            ? WorldCellKey.FromLumonSceneNear(coord.ToKey())
            : WorldCellKey.FromLumonSceneFar(coord.ToKey());

        if (cells.TryGetValue(key, out LumonSceneRegionCell? existing))
        {
            return existing;
        }

        var created = new LumonSceneRegionCell(kind, in coord);
        cells.Add(key, created);
        return created;
    }

    public bool TryPopTransition(out WorldCellKey key)
        => transitions.TryPopMax(out key);

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
            case WorldCellWorkQueue.StateTransition:
                transitions.Upsert(key, priority);
                break;

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
            case WorldCellWorkQueue.StateTransition:
                transitions.Remove(key);
                break;

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
            .Append(" qT=").Append(TransitionCount)
            .Append(" qC=").Append(CaptureCount)
            .Append(" qR=").Append(RelightCount)
            .Append(" now=").Append(nowTick);

        AppendQueueDump(sb, "T", transitions, topN);
        AppendQueueDump(sb, "C", capture, topN);
        AppendQueueDump(sb, "R", relight, topN);

        return sb.ToString();
    }

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
}
