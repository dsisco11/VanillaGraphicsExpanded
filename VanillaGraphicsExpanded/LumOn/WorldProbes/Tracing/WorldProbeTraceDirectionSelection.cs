using System;
using System.Numerics;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

/// <summary>Shares deterministic atlas and nearby-occupancy directions between tracing backends.</summary>
internal static class WorldProbeTraceDirectionSelection
{
    #region Direction selection
    /// <summary>Fills the admitted atlas subset without changing exploration or importance policy.</summary>
    public static int Fill(in LumOnWorldProbeTraceWorkItem item, Span<int> destination)
    {
        int size = Math.Max(1, item.WorldProbeOctahedralTileSize);
        int count = Math.Clamp(item.WorldProbeAtlasTexelsPerUpdate, 1, checked(size * size));
        return item.EnableDirectionPIS
            ? LumOnWorldProbeAtlasDirectionSlicing.FillTexelIndicesForUpdateImportance(
                item.FrameIndex, item.Request.StorageLinearIndex, size, count, Vector3.UnitY,
                item.DirectionPISExploreFraction, item.DirectionPISExploreCount, item.DirectionPISWeightEpsilon,
                LumOnWorldProbeAtlasDirections.GetDirections(size), destination)
            : LumOnWorldProbeAtlasDirectionSlicing.FillTexelIndicesForUpdate(
                item.FrameIndex, item.Request.StorageLinearIndex, size, count, destination);
    }

    /// <summary>Reports whether this admission still needs the short cardinal importance query.</summary>
    public static bool NeedsNearby(in LumOnWorldProbeTraceWorkItem item) => item.NearbySolidHitDistance > 0 &&
        (item.Request.ImportanceFlags & LumOnWorldProbeImportanceFlags.NearbySolidHit) == 0;

    /// <summary>Decodes the retained signed cardinal axis on either tracing backend.</summary>
    public static Vector3 CardinalDirection(int index)
    {
        return index switch
        {
            0 => Vector3.UnitX, 1 => -Vector3.UnitX, 2 => Vector3.UnitY,
            3 => -Vector3.UnitY, 4 => Vector3.UnitZ, 5 => -Vector3.UnitZ,
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };
    }

    /// <summary>Encodes the shared cardinal axis selection, including negative frame or storage offsets.</summary>
    public static int NearbyIndex(in LumOnWorldProbeTraceWorkItem item)
    {
        int index = (int)(((long)item.FrameIndex + item.Request.StorageLinearIndex) % 6);
        return index < 0 ? index + 6 : index;
    }
    #endregion
}
