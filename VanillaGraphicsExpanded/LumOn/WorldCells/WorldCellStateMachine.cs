using System;

namespace VanillaGraphicsExpanded.LumOn.WorldCells;

internal enum WorldCellTransitionAction : byte
{
    None = 0,

    EnsureUnloaded = 1,
    EnsureLoaded = 2,
    EnsureActive = 3,

    /// <summary>
    /// Move from active participation to loaded residency (hysteresis).
    /// </summary>
    DeactivateToLoaded = 4,
}

/// <summary>
/// Lightweight helper for converting desired/actual states into next transition actions.
/// Phase 9 scaffolding: call sites decide what work to enqueue.
/// </summary>
internal static class WorldCellStateMachine
{
    public static bool TryGetNextAction(IWorldCell cell, in WorldCellStateTransitionContext context, out WorldCellTransitionAction action)
    {
        if (cell is null) throw new ArgumentNullException(nameof(cell));

        // Respect cooldown/backoff.
        if (context.NowTick < cell.NextEligibleTick)
        {
            action = WorldCellTransitionAction.None;
            return false;
        }

        return TryGetNextAction(cell.DesiredState, cell.ActualState, out action);
    }

    public static bool TryGetNextAction(WorldCellDesiredState desired, WorldCellActualState actual, out WorldCellTransitionAction action)
    {
        // Desired dominates: always converge Actual toward Desired.
        switch (desired)
        {
            case WorldCellDesiredState.Unloaded:
                action = actual == WorldCellActualState.Unloaded ? WorldCellTransitionAction.None : WorldCellTransitionAction.EnsureUnloaded;
                return action != WorldCellTransitionAction.None;

            case WorldCellDesiredState.Loaded:
                action = actual switch
                {
                    WorldCellActualState.Unloaded => WorldCellTransitionAction.EnsureLoaded,
                    WorldCellActualState.Loading => WorldCellTransitionAction.None,
                    WorldCellActualState.Loaded => WorldCellTransitionAction.None,
                    WorldCellActualState.Activating => WorldCellTransitionAction.None,
                    WorldCellActualState.Active => WorldCellTransitionAction.DeactivateToLoaded,
                    WorldCellActualState.Unloading => WorldCellTransitionAction.None,
                    _ => WorldCellTransitionAction.None,
                };
                return action != WorldCellTransitionAction.None;

            case WorldCellDesiredState.Active:
                action = actual switch
                {
                    WorldCellActualState.Unloaded => WorldCellTransitionAction.EnsureLoaded,
                    WorldCellActualState.Loading => WorldCellTransitionAction.None,
                    WorldCellActualState.Loaded => WorldCellTransitionAction.EnsureActive,
                    WorldCellActualState.Activating => WorldCellTransitionAction.None,
                    WorldCellActualState.Active => WorldCellTransitionAction.None,
                    WorldCellActualState.Unloading => WorldCellTransitionAction.None,
                    _ => WorldCellTransitionAction.None,
                };
                return action != WorldCellTransitionAction.None;

            default:
                action = WorldCellTransitionAction.None;
                return false;
        }
    }
}
