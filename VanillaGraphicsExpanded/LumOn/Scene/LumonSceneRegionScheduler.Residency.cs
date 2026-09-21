using System;
using System.Collections.Generic;
using System.Linq;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Maps scene slot residency and heat to independent coordinator registrations.</summary>
internal sealed partial class LumonSceneRegionScheduler
{
    private readonly Dictionary<WorldCellKind, long> instances = new();
    private readonly Dictionary<WorldCellKey, long> heatSources = new();
    private long nextHeatSource = 1;

    #region Residency API
    /// <summary>Transfers loaded/active windows and heat to the coordinator, then acknowledges assigned GPU slots.</summary>
    public void UpdateResidency(in WorldCellStateTransitionContext state, in WorldCellPriorityContext priority)
    {
        UpdateCoverage(state);
        WorldCellPriorityContext ordering = priority;

        // Slots are already coherently assigned by the GPU backend. Residency says nothing about page lighting validity.
        foreach (LumonSceneRegionCell cell in cells.Values
            .OrderByDescending(c => worldPartition.TryGetCell(PartitionKey(c), out PartitionCellInfo info) ? info.Desired : PartitionResidency.Unloaded)
            .ThenByDescending(c => c.CalculatePriority(ordering)).ToArray())
        {
            PartitionCellKey key = PartitionKey(cell);
            if (!worldPartition.TryGetCell(key, out PartitionCellInfo info)) continue;
            if (cell.HasAssignedSlot && cell.NextEligibleTick <= nowTick && !info.Ready &&
                worldPartition.TryBeginUpdate(key, out PartitionRequest? request))
            {
                uint slot = cell.ChunkSlot;
                ushort generation = cell.SlotGeneration;
                worldPartition.TryPublishUpdate(request!, 0,
                    () => cell.HasAssignedSlot && cell.ChunkSlot == slot && cell.SlotGeneration == generation, () => true);
            }
            if (worldPartition.TryGetCell(key, out info))
            {
                cell.DesiredState = (WorldCellDesiredState)info.Desired;
                cell.ActualState = (info.Ready ? info.Actual : PartitionResidency.Unloaded) switch
                {
                    PartitionResidency.Active => WorldCellActualState.Active,
                    PartitionResidency.Loaded => WorldCellActualState.Loaded,
                    _ => WorldCellActualState.Unloaded
                };
            }
            cell.EnqueueStateAndWork(this, ordering);
        }
    }

    /// <summary>Applies the scene's source policy before a GPU ring remap can recycle departing slots.</summary>
    public void UpdateCoverage(in WorldCellStateTransitionContext state)
    {
        nowTick = state.NowTick;
        WorldCellStateTransitionContext context = state;
        foreach (WorldCellKind kind in cells.Keys.Select(k => k.Kind).Distinct().ToArray())
        {
            long instance = Instance(kind);
            PartitionBounds loaded = Bounds(context.LoadedWindowMinRegion, context.LoadedWindowMaxRegion);
            PartitionBounds active = context.HasActiveWindow
                ? Bounds(context.ActiveWindowMinRegion, context.ActiveWindowMaxRegion) : loaded;
            worldPartition.SetSource(new(0, instance, "primary", active.Min, active, Loaded: loaded));
            foreach (LumonSceneRegionCell cell in cells.Values.Where(c => c.Kind == kind))
            {
                bool hot = cell.HeatUntilTick > nowTick && Contains(loaded, cell.ChunkCoordInt3);
                if (hot)
                {
                    if (!heatSources.TryGetValue(cell.Key, out long id)) heatSources[cell.Key] = id = nextHeatSource++;
                    PartitionBounds bounds = Bounds(cell.ChunkCoordInt3, cell.ChunkCoordInt3);
                    worldPartition.SetSource(new(id, instance, "primary", bounds.Min, bounds));
                }
                else if (heatSources.Remove(cell.Key, out long id)) worldPartition.RemoveSource(instance, id);
            }
            worldPartition.RefreshCoverage(instance);
        }

    }

    /// <summary>Invalidates a changed slot generation before acknowledging its replacement.</summary>
    public void UpdateSlot(LumonSceneRegionCell cell, uint slot, ushort generation, long tick)
    {
        bool changed = cell.HasAssignedSlot && (cell.ChunkSlot != slot || cell.SlotGeneration != generation);
        cell.UpdateSlotAssignment(slot, generation, tick);
        if (changed) worldPartition.Dirty(PartitionKey(cell));
    }

    /// <summary>Ends registrations before clearing domain queues; late publications cannot enter the next world.</summary>
    private void ReleasePartitions()
    {
        foreach (long instance in instances.Values) worldPartition.Unregister(instance);
        instances.Clear();
        heatSources.Clear();
    }
    #endregion

    #region Backend acknowledgements
    /// <summary>Acknowledges activation only after a stable slot and its existing cooldown allow participation.</summary>
    bool IPartitionResidencyBackend.SetActive(in PartitionCellKey key, bool active) =>
        TryFind(key, out LumonSceneRegionCell? cell) && cell.HasAssignedSlot && (!active || cell.NextEligibleTick <= nowTick);

    /// <summary>Removes domain eligibility while residency contents are invalidated.</summary>
    void IPartitionResidencyBackend.Invalidate(in PartitionCellKey key)
    {
        if (!TryFind(key, out LumonSceneRegionCell? cell)) return;
        cell.ActualState = WorldCellActualState.Unloaded;
        capture.Remove(cell.Key);
        relight.Remove(cell.Key);
    }

    /// <summary>Releases only domain bookkeeping; the slot backend owns physical ring storage.</summary>
    void IPartitionResidencyBackend.Retire(in PartitionCellKey key)
    {
        if (!TryFind(key, out LumonSceneRegionCell? cell)) return;
        retireSlot?.Invoke(cell.ChunkCoord);
        cells.Remove(cell.Key);
    }
    #endregion

    #region Identity and bounds
    /// <summary>Registers near and far scenes separately so packed domain keys are never global identity.</summary>
    private long Instance(WorldCellKind kind)
    {
        if (!instances.TryGetValue(kind, out long instance))
            instances[kind] = instance = worldPartition.Register(kind.ToString(), "primary", new(new(32, 32, 32)),
                new(0, 0, 0), new(16384, 64, 64, 64, 0), this);
        return instance;
    }

    /// <summary>Maps a domain content record to its independent logical residency key.</summary>
    private PartitionCellKey PartitionKey(LumonSceneRegionCell cell) => new(Instance(cell.Kind), "primary",
        new(cell.ChunkCoord.X, cell.ChunkCoord.Y, cell.ChunkCoord.Z));

    /// <summary>Finds domain data without maintaining another residency registry.</summary>
    private bool TryFind(in PartitionCellKey key, out LumonSceneRegionCell cell)
    {
        foreach ((WorldCellKind kind, long instance) in instances)
        {
            if (instance != key.Instance) continue;
            var coordinate = new LumonSceneChunkCoord(checked((int)key.Coordinate.X), checked((int)key.Coordinate.Y), checked((int)key.Coordinate.Z));
            return cells.TryGetValue(new(kind, coordinate.ToKey()), out cell!);
        }
        cell = null!;
        return false;
    }

    /// <summary>Converts inclusive chunk bounds to half-open world-zero partition bounds.</summary>
    private static PartitionBounds Bounds(in VectorInt3 min, in VectorInt3 max) =>
        new(new(min.X * 32d, min.Y * 32d, min.Z * 32d), new((max.X + 1d) * 32, (max.Y + 1d) * 32, (max.Z + 1d) * 32));

    /// <summary>Restricts feedback heat to the current loaded slot envelope.</summary>
    private static bool Contains(in PartitionBounds bounds, in VectorInt3 coordinate) =>
        coordinate.X * 32d >= bounds.Min.X && coordinate.X * 32d < bounds.Max.X &&
        coordinate.Y * 32d >= bounds.Min.Y && coordinate.Y * 32d < bounds.Max.Y &&
        coordinate.Z * 32d >= bounds.Min.Z && coordinate.Z * 32d < bounds.Max.Z;
    #endregion
}
