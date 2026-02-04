using System;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.LumOn.WorldCells;

/// <summary>
/// Tracks <see cref="IWorldCell"/> instances and provides simple query helpers by kind/state.
/// </summary>
/// <remarks>
/// This is intentionally minimal (Phase 0):
/// - no spatial acceleration structure
/// - no background streaming
/// - callers own desired-state evaluation and state-machine driving
/// </remarks>
internal sealed class WorldPartitionSystem
{
    private readonly object gate = new();
    private readonly Dictionary<WorldCellKey, IWorldCell> cells = new();

    public int CellCount
    {
        get
        {
            lock (gate)
            {
                return cells.Count;
            }
        }
    }

    public bool TryGet(WorldCellKey key, out IWorldCell cell)
    {
        lock (gate)
        {
            return cells.TryGetValue(key, out cell!);
        }
    }

    public TCell GetOrCreate<TCell>(WorldCellKey key, Func<TCell> factory)
        where TCell : class, IWorldCell
    {
        if (factory is null) throw new ArgumentNullException(nameof(factory));

        lock (gate)
        {
            if (cells.TryGetValue(key, out IWorldCell? existing))
            {
                return (TCell)existing;
            }

            TCell created = factory();
            cells.Add(key, created);
            return created;
        }
    }

    public bool Remove(WorldCellKey key)
    {
        lock (gate)
        {
            return cells.Remove(key);
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            cells.Clear();
        }
    }

    public int CopyKeysByDesiredState(WorldCellKind kind, WorldCellDesiredState desired, Span<WorldCellKey> dst)
    {
        if (dst.Length <= 0)
        {
            return 0;
        }

        int written = 0;
        lock (gate)
        {
            if (cells.Count <= 0)
            {
                return 0;
            }

            foreach (var kvp in cells)
            {
                IWorldCell cell = kvp.Value;
                if (cell.Kind != kind || cell.DesiredState != desired)
                {
                    continue;
                }

                if ((uint)written >= (uint)dst.Length)
                {
                    break;
                }

                dst[written++] = kvp.Key;
            }
        }

        return written;
    }

    public int CopyKeysByActualState(WorldCellKind kind, WorldCellActualState actual, Span<WorldCellKey> dst)
    {
        if (dst.Length <= 0)
        {
            return 0;
        }

        int written = 0;
        lock (gate)
        {
            if (cells.Count <= 0)
            {
                return 0;
            }

            foreach (var kvp in cells)
            {
                IWorldCell cell = kvp.Value;
                if (cell.Kind != kind || cell.ActualState != actual)
                {
                    continue;
                }

                if ((uint)written >= (uint)dst.Length)
                {
                    break;
                }

                dst[written++] = kvp.Key;
            }
        }

        return written;
    }
}
