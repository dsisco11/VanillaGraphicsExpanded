using System;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Bounds independently configured lighting allocations by the shared full-tile publication ceiling.</summary>
internal readonly record struct SurfaceLightingBudgets(int Seed, int Direct, int Indirect)
{
    #region Allocation
    /// <summary>Distributes scarce publication slots fairly without exceeding any requested stage allocation.</summary>
    public static SurfaceLightingBudgets Resolve(int seed, int direct, int indirect, int tile, int frame)
    {
        Span<int> requested = stackalloc int[] { Math.Clamp(seed, 0, 256), Math.Clamp(direct, 0, 256), Math.Clamp(indirect, 0, 256) };
        Span<int> allocated = stackalloc int[3];
        allocated.Clear();
        int remaining = Math.Min(65536 / checked(tile * tile), requested[0] + requested[1] + requested[2]);
        // Rotate the starting stage so even a one-tile ceiling cannot permanently exclude a stage.
        int start = (int)((uint)frame % 3);
        while (remaining > 0)
            for (int i = 0; i < 3 && remaining > 0; i++)
            {
                int stage = (start + i) % 3;
                if (allocated[stage] >= requested[stage]) continue;
                allocated[stage]++; remaining--;
            }
        return new(allocated[0], allocated[1], allocated[2]);
    }
    #endregion
}
