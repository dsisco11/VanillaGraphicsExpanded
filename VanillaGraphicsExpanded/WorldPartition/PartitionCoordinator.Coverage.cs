using System;
using System.Collections.Generic;
using System.Linq;

namespace VanillaGraphicsExpanded.WorldPartition;

/// <summary>Incremental source coverage and bounded retention for registered grids.</summary>
internal sealed partial class PartitionCoordinator
{
    #region Coverage selection
    /// <summary>Applies source range differences and preserves all overlapping cell lifetimes.</summary>
    private void UpdateCoverage(PartitionRegistration registration)
    {
        if (registration.CoverageDirty)
        {
            registration.CoverageDirty = false;
            registration.CoverageEvaluations++;
            var touched = new HashSet<PartitionCoordinate>();
            foreach (long id in registration.SourceCoverage.Keys.Union(registration.Sources.Keys).ToArray())
            {
                registration.SourceCoverage.TryGetValue(id, out PartitionSourceCoverage old);
                PartitionSourceCoverage next = default;
                if (registration.Sources.TryGetValue(id, out PartitionSource? source))
                    next = new(registration.Layout.Range(source.Required), registration.Layout.Range(source.Required.Expand(registration.Coverage.PrefetchMargin)));
                ApplyRangeDifference(registration, id, old.Required, next.Required, true, touched);
                ApplyRangeDifference(registration, id, old.Prefetch, next.Prefetch, false, touched);
                if (source != null) registration.SourceCoverage[id] = next;
                else registration.SourceCoverage.Remove(id);
            }
            foreach (PartitionCoordinate coordinate in touched)
            {
                PartitionCellState cell = registration.Cells[coordinate];
                if (cell.RequiredSources.Count > 0 || cell.PrefetchSources.Count > 0)
                {
                    cell.Desired = cell.RequiredSources.Count > 0 ? PartitionResidency.Active : PartitionResidency.Loaded;
                    registration.Retained.Remove(coordinate);
                }
                else
                {
                    // Newly departed cells start their retention clock now, not when the source first arrived.
                    cell.LastRequested = tick;
                    if (cell.Ready) registration.Retained.Add(coordinate);
                    else RemoveCell(registration, cell);
                }
            }
        }
        // Only the retained fringe needs time/spatial checks on unchanged frames.
        foreach (PartitionCoordinate coordinate in registration.Retained.ToArray())
        {
            PartitionCellState cell = registration.Cells[coordinate];
            bool retain = cell.Ready && tick - cell.LastRequested <= registration.Coverage.RetentionTicks &&
                registration.Sources.Values.Any(s => Intersects(registration.Layout.Bounds(coordinate), s.Required.Expand(registration.Coverage.RetentionMargin)));
            if (retain) cell.Desired = PartitionResidency.Loaded;
            else RemoveCell(registration, cell);
        }
    }

    /// <summary>Updates membership only for changed slabs of one source's required or prefetch range.</summary>
    private void ApplyRangeDifference(PartitionRegistration r, long sourceId, in PartitionCellRange old, in PartitionCellRange next,
        bool required, HashSet<PartitionCoordinate> touched)
    {
        foreach (PartitionCoordinate coordinate in old.Except(next))
        {
            PartitionCellState cell = r.Cells[coordinate];
            (required ? cell.RequiredSources : cell.PrefetchSources).Remove(sourceId);
            touched.Add(coordinate);
            r.CoverageCellVisits++;
        }
        foreach (PartitionCoordinate coordinate in next.Except(old))
        {
            if (!r.Cells.TryGetValue(coordinate, out PartitionCellState? cell))
            {
                cell = new PartitionCellState { Key = new(r.Instance, r.World, coordinate), Incarnation = ++nextIncarnation, WaitingSince = tick };
                r.Cells.Add(coordinate, cell);
            }
            (required ? cell.RequiredSources : cell.PrefetchSources).Add(sourceId);
            touched.Add(coordinate);
            r.CoverageCellVisits++;
        }
    }

    /// <summary>Computes current source priority and distance for pending work without walking cell interiors for coverage.</summary>
    private static double Priority(PartitionRegistration r, PartitionCellState cell)
    {
        double priority = -1024;
        PartitionPoint min = r.Layout.Bounds(cell.Key.Coordinate).Min;
        foreach (long id in cell.PrefetchSources)
        {
            PartitionSource source = r.Sources[id];
            double distance = Math.Abs(min.X - source.Position.X) + Math.Abs(min.Y - source.Position.Y) + Math.Abs(min.Z - source.Position.Z);
            priority = Math.Max(priority, Math.Clamp(source.Priority - distance, -1024, 1024));
        }
        return priority;
    }

    /// <summary>Checks half-open overlap without converting positions to floats.</summary>
    private static bool Intersects(in PartitionBounds a, in PartitionBounds b) => a.Min.X < b.Max.X && a.Max.X > b.Min.X &&
        a.Min.Y < b.Max.Y && a.Max.Y > b.Min.Y && a.Min.Z < b.Max.Z && a.Max.Z > b.Min.Z;

    /// <summary>Ends the logical incarnation after rendering contents have been retired.</summary>
    private static void RemoveCell(PartitionRegistration registration, PartitionCellState cell)
    {
        cell.Desired = PartitionResidency.Unloaded;
        Retire(registration, cell);
        registration.Cells.Remove(cell.Key.Coordinate);
        registration.Retained.Remove(cell.Key.Coordinate);
    }
    #endregion
}
