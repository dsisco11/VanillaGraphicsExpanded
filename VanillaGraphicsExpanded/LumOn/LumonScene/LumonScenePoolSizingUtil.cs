using System;

namespace VanillaGraphicsExpanded.LumOn.LumonScene;

internal static class LumonScenePoolSizingUtil
{
    /// <summary>
    /// Computes the number of chunks covered by a field expressed as a Chebyshev radius in chunk space (square in XZ),
    /// plus an extra margin equal to (edgeChunks * 2) to absorb re-anchors without immediate eviction thrash.
    /// </summary>
    public static LumonSceneFieldChunkBudget ComputeSquareFieldBudget(int radiusChunks)
    {
        if (radiusChunks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(radiusChunks));
        }

        // side = 2R + 1
        int side = checked(radiusChunks * 2 + 1);
        int covered = checked(side * side);

        // Extra margin: two edges worth of chunks.
        int extra = checked(side * 2);
        int total = checked(covered + extra);

        return new LumonSceneFieldChunkBudget(radiusChunks, side, covered, extra, total);
    }

    /// <summary>
    /// Converts a field chunk budget into a guaranteed-resident physical page budget under the rule:
    /// "1 guaranteed resident page per chunk".
    /// </summary>
    public static int ComputeGuaranteedResidentPages(in LumonSceneFieldChunkBudget budget)
        => budget.TotalChunks;

    /// <summary>
    /// Computes the chunk count for a "far field annulus" (chunks with distance in (near, far]) plus the same extra margin
    /// based on the far field edge length.
    /// </summary>
    public static LumonSceneFieldChunkBudget ComputeFarAnnulusBudget(int nearRadiusChunks, int farRadiusChunks)
    {
        if (nearRadiusChunks < 0) throw new ArgumentOutOfRangeException(nameof(nearRadiusChunks));
        if (farRadiusChunks < 0) throw new ArgumentOutOfRangeException(nameof(farRadiusChunks));
        if (farRadiusChunks < nearRadiusChunks) throw new ArgumentOutOfRangeException(nameof(farRadiusChunks));

        LumonSceneFieldChunkBudget far = ComputeSquareFieldBudget(farRadiusChunks);
        LumonSceneFieldChunkBudget near = ComputeSquareFieldBudget(nearRadiusChunks);

        int coveredAnnulus = far.CoveredChunks - near.CoveredChunks;
        int total = checked(coveredAnnulus + far.ExtraChunks);

        return new LumonSceneFieldChunkBudget(
            radiusChunks: farRadiusChunks,
            sideChunks: far.SideChunks,
            coveredChunks: coveredAnnulus,
            extraChunks: far.ExtraChunks,
            totalChunks: total);
    }

    public static int ComputeGuaranteedResidentPagesSquareField(int radiusChunks)
        => ComputeGuaranteedResidentPages(ComputeSquareFieldBudget(radiusChunks));

    public static int ComputeGuaranteedResidentPagesFarAnnulus(int nearRadiusChunks, int farRadiusChunks)
        => ComputeGuaranteedResidentPages(ComputeFarAnnulusBudget(nearRadiusChunks, farRadiusChunks));
}
