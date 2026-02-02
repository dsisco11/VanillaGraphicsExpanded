using System;

using VanillaGraphicsExpanded.LumOn.WorldCells;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;

namespace VanillaGraphicsExpanded.LumOn.Scene;

internal sealed class TraceSceneRegionCell : WorldCell
{
    public TraceSceneRegionCell(ChunkKey chunkKey)
        : base(WorldCellKey.FromTraceSceneRegion(chunkKey))
    {
        ChunkKey = chunkKey;
        chunkKey.Decode(out int x, out int y, out int z);
        RegionCoord = new VectorInt3(x, y, z);
    }

    public TraceSceneRegionCell(VectorInt3 regionCoord)
        : this(ChunkKey.FromChunkCoords(regionCoord.X, regionCoord.Y, regionCoord.Z))
    {
    }

    public ChunkKey ChunkKey { get; }

    public VectorInt3 RegionCoord { get; }

    public int MissingStreak { get; set; }

    public long LastSeenLoadedTick { get; set; }

    public int InFlightVersion { get; set; }

    public TraceSceneRegionPriorityReason LastPriorityReasons { get; private set; }

    public override float CalculatePriority(in WorldCellPriorityContext context)
    {
        TraceSceneRegionPriorityReason reasons = TraceSceneRegionPriorityReason.None;

        if (context.HasWindow)
        {
            bool inWindow = RegionCoord.X >= context.WindowMinRegion.X
                            && RegionCoord.Y >= context.WindowMinRegion.Y
                            && RegionCoord.Z >= context.WindowMinRegion.Z
                            && RegionCoord.X <= context.WindowMaxRegion.X
                            && RegionCoord.Y <= context.WindowMaxRegion.Y
                            && RegionCoord.Z <= context.WindowMaxRegion.Z;

            if (!inWindow)
            {
                LastPriorityReasons = TraceSceneRegionPriorityReason.None;
                return float.NegativeInfinity;
            }

            reasons |= TraceSceneRegionPriorityReason.InWindow;
        }

        if (context.NowTick < NextEligibleTick)
        {
            reasons |= TraceSceneRegionPriorityReason.CooldownSuppressed;
            LastPriorityReasons = reasons;
            return float.NegativeInfinity;
        }

        VectorInt3 anchorBlock = context.HasAnchor ? context.AnchorBlockPos : context.CameraBlockPos;
        VectorInt3 anchorRegion = LumonSceneTraceSceneClipmapMath.WorldCellToRegionCoord(anchorBlock);

        int dx = RegionCoord.X - anchorRegion.X;
        int dy = RegionCoord.Y - anchorRegion.Y;
        int dz = RegionCoord.Z - anchorRegion.Z;

        long dist2l = (long)dx * dx + (long)dy * dy + (long)dz * dz;
        float dist2 = dist2l <= int.MaxValue ? dist2l : int.MaxValue;

        float priority = TraceSceneRegionPriorityConstants.DistanceWeight / (1.0f + dist2);
        reasons |= TraceSceneRegionPriorityReason.Distance;

        if (AppliedVersion != CurrentVersion)
        {
            priority += TraceSceneRegionPriorityConstants.StaleBonus;
            reasons |= TraceSceneRegionPriorityReason.Stale;

            if (AppliedVersion == 0)
            {
                priority += TraceSceneRegionPriorityConstants.NeverAppliedBonus;
                reasons |= TraceSceneRegionPriorityReason.NeverApplied;
            }
        }

        if (LastSeenLoadedTick > 0 && (context.NowTick - LastSeenLoadedTick) <= TraceSceneRegionPriorityConstants.SeenLoadedRecentTicks)
        {
            priority += TraceSceneRegionPriorityConstants.SeenLoadedRecentlyBonus;
            reasons |= TraceSceneRegionPriorityReason.SeenLoadedRecently;
        }

        if (MissingStreak > 0)
        {
            priority -= TraceSceneRegionPriorityConstants.MissingPenaltyPerStreak * MissingStreak;
            reasons |= TraceSceneRegionPriorityReason.MissingPenalty;
        }

        LastPriorityReasons = reasons;
        return priority;
    }
}
