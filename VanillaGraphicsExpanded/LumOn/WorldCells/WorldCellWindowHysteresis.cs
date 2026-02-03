using System;
using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.LumOn.WorldCells;

/// <summary>
/// Generic helper for dual-window (Loaded/Active) hysteresis.
///
/// This is intentionally policy-light:
/// - Callers provide windows and optional hold durations.
/// - Callers can add additional signals ("heat", loadedness hints, budget pressure) on top.
/// </summary>
internal static class WorldCellWindowHysteresis
{
    public static WorldCellDesiredState CalculateDesiredState(
        VectorInt3 cellCoord,
        in WorldCellStateTransitionContext context,
        ref long activeHoldUntilTick,
        int activeHoldTicks)
    {
        activeHoldTicks = Math.Max(0, activeHoldTicks);

        if (context.HasLoadedWindow)
        {
            bool inLoaded = IsWithinInclusive(cellCoord, context.LoadedWindowMinRegion, context.LoadedWindowMaxRegion);
            if (!inLoaded)
            {
                return WorldCellDesiredState.Unloaded;
            }
        }

        if (context.HasActiveWindow)
        {
            bool inActive = IsWithinInclusive(cellCoord, context.ActiveWindowMinRegion, context.ActiveWindowMaxRegion);
            if (inActive)
            {
                if (activeHoldTicks > 0)
                {
                    activeHoldUntilTick = Math.Max(activeHoldUntilTick, context.NowTick + activeHoldTicks);
                }

                return WorldCellDesiredState.Active;
            }

            if (activeHoldUntilTick > context.NowTick)
            {
                return WorldCellDesiredState.Active;
            }

            return WorldCellDesiredState.Loaded;
        }

        // With no windows, default to Active.
        return WorldCellDesiredState.Active;
    }

    private static bool IsWithinInclusive(VectorInt3 p, VectorInt3 min, VectorInt3 max)
        => p.X >= min.X && p.Y >= min.Y && p.Z >= min.Z
           && p.X <= max.X && p.Y <= max.Y && p.Z <= max.Z;
}
