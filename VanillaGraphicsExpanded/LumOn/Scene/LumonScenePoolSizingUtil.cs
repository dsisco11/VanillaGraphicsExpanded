using System;

using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.LumOn.Scene;

internal static class LumonScenePoolSizingUtil
{
    /// <summary>
    /// Computes the chunk budget for a field expressed as a Chebyshev radius in chunk space (box in XYZ),
    /// plus an extra margin equal to (maxEdgeChunks * 2) to absorb re-anchors without immediate eviction thrash.
    /// </summary>
    public static LumonSceneFieldChunkBudget ComputeBoxFieldBudget(int radiusXZChunks, int radiusYChunks)
    {
        if (radiusXZChunks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(radiusXZChunks));
        }

        if (radiusYChunks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(radiusYChunks));
        }

        // dims = 2R + 1 per axis
        int dimX = checked(radiusXZChunks * 2 + 1);
        int dimY = checked(radiusYChunks * 2 + 1);
        int dimZ = checked(radiusXZChunks * 2 + 1);
        var dims = new VectorInt3(dimX, dimY, dimZ);

        int covered = checked(dimX * checked(dimY * dimZ));

        // Extra margin: two edges worth of chunks (per the "edgeChunks * 2" requirement).
        int edge = Math.Max(dimX, Math.Max(dimY, dimZ));
        int extra = checked(edge * 2);
        int total = checked(covered + extra);

        return new LumonSceneFieldChunkBudget(radiusXZChunks, radiusYChunks, dims, covered, extra, total);
    }

    public static LumonSceneFieldChunkBudget ComputeSquareFieldBudget(int radiusChunks)
        => ComputeBoxFieldBudget(radiusXZChunks: radiusChunks, radiusYChunks: 0);

    /// <summary>
    /// Converts a field chunk budget into a guaranteed-resident physical page budget under the rule:
    /// "1 guaranteed resident page per chunk".
    /// </summary>
    public static int ComputeGuaranteedResidentPages(in LumonSceneFieldChunkBudget budget)
        => budget.TotalChunks;

    public static int ComputeChunkSlotCountBoxField(int radiusXZChunks, int radiusYChunks)
        => ComputeBoxFieldBudget(radiusXZChunks, radiusYChunks).CoveredChunks;

    public static int ComputeChunkSlotCountSquareField(int radiusChunks)
        => ComputeChunkSlotCountBoxField(radiusXZChunks: radiusChunks, radiusYChunks: 0);

    /// <summary>
    /// Computes the chunk count for a "far field annulus" (chunks with distance in (near, far]) plus the same extra margin
    /// based on the far field max edge length.
    /// </summary>
    public static LumonSceneFieldChunkBudget ComputeFarAnnulusBudget(int nearRadiusXZChunks, int nearRadiusYChunks, int farRadiusXZChunks, int farRadiusYChunks)
    {
        if (nearRadiusXZChunks < 0) throw new ArgumentOutOfRangeException(nameof(nearRadiusXZChunks));
        if (nearRadiusYChunks < 0) throw new ArgumentOutOfRangeException(nameof(nearRadiusYChunks));
        if (farRadiusXZChunks < 0) throw new ArgumentOutOfRangeException(nameof(farRadiusXZChunks));
        if (farRadiusYChunks < 0) throw new ArgumentOutOfRangeException(nameof(farRadiusYChunks));
        if (farRadiusXZChunks < nearRadiusXZChunks) throw new ArgumentOutOfRangeException(nameof(farRadiusXZChunks));
        if (farRadiusYChunks < nearRadiusYChunks) throw new ArgumentOutOfRangeException(nameof(farRadiusYChunks));

        LumonSceneFieldChunkBudget far = ComputeBoxFieldBudget(farRadiusXZChunks, farRadiusYChunks);
        LumonSceneFieldChunkBudget near = ComputeBoxFieldBudget(nearRadiusXZChunks, nearRadiusYChunks);

        int coveredAnnulus = far.CoveredChunks - near.CoveredChunks;
        int total = checked(coveredAnnulus + far.ExtraChunks);

        return new LumonSceneFieldChunkBudget(
            RadiusXZChunks: farRadiusXZChunks,
            RadiusYChunks: farRadiusYChunks,
            DimsChunks: far.DimsChunks,
            CoveredChunks: coveredAnnulus,
            ExtraChunks: far.ExtraChunks,
            TotalChunks: total);
    }

    public static LumonSceneFieldChunkBudget ComputeFarAnnulusBudget(int nearRadiusChunks, int farRadiusChunks)
        => ComputeFarAnnulusBudget(nearRadiusChunks, 0, farRadiusChunks, 0);

    public static int ComputeGuaranteedResidentPagesSquareField(int radiusChunks)
        => ComputeGuaranteedResidentPages(ComputeSquareFieldBudget(radiusChunks));

    public static int ComputeGuaranteedResidentPagesBoxField(int radiusXZChunks, int radiusYChunks)
        => ComputeGuaranteedResidentPages(ComputeBoxFieldBudget(radiusXZChunks, radiusYChunks));

    public static int ComputeGuaranteedResidentPagesFarAnnulus(int nearRadiusChunks, int farRadiusChunks)
        => ComputeGuaranteedResidentPages(ComputeFarAnnulusBudget(nearRadiusChunks, farRadiusChunks));

    public static int ComputeGuaranteedResidentPagesFarAnnulus(int nearRadiusXZChunks, int nearRadiusYChunks, int farRadiusXZChunks, int farRadiusYChunks)
        => ComputeGuaranteedResidentPages(ComputeFarAnnulusBudget(nearRadiusXZChunks, nearRadiusYChunks, farRadiusXZChunks, farRadiusYChunks));
}
