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
    public static void NotifyTransitionSucceeded(IWorldCell cell)
    {
        if (cell is null) throw new ArgumentNullException(nameof(cell));

        if (cell is WorldCell wc)
        {
            wc.CooldownStreak = 0;
        }
    }

    public static void NotifyTransitionFailed(IWorldCell cell, long nowTick, long minCooldownTicks = 2, long maxCooldownTicks = 120)
    {
        if (cell is null) throw new ArgumentNullException(nameof(cell));

        if (nowTick <= 0)
        {
            return;
        }

        if (cell is not WorldCell wc)
        {
            // Best-effort: without streak bookkeeping, apply a small bounded cooldown.
            long cooldownTicks = Math.Clamp(minCooldownTicks, 0, maxCooldownTicks);
            cell.NextEligibleTick = Math.Max(cell.NextEligibleTick, nowTick + cooldownTicks);
            return;
        }

        if (nowTick - wc.LastAttemptTick > 1)
        {
            wc.CooldownStreak = 0;
        }

        wc.LastAttemptTick = nowTick;
        wc.CooldownStreak = Math.Min(8, wc.CooldownStreak + 1);

        // Exponential backoff: min*2^streak, clamped.
        int shift = Math.Min(6, wc.CooldownStreak);
        long cdExp = minCooldownTicks <= 0 ? 0 : checked(minCooldownTicks << shift);
        long cooldownTicksExp = Math.Min(maxCooldownTicks, cdExp);
        cell.NextEligibleTick = Math.Max(cell.NextEligibleTick, nowTick + cooldownTicksExp);
    }

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
