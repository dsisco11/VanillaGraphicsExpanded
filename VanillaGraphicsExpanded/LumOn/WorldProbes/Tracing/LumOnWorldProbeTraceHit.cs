using System.Numerics;

using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

// Matches base-game face index order: N=0, E=1, S=2, W=3, U=4, D=5
// (see Vintagestory.API.MathTools.BlockFacing.index* constants).
internal enum ProbeHitFace : byte
{
    North = 0,
    East = 1,
    South = 2,
    West = 3,
    Up = 4,
    Down = 5,
}

internal static class ProbeHitFaceUtil
{
    public static ProbeHitFace FromAxisNormal(VectorInt3 axisNormal)
    {
        return axisNormal switch
        {
            { X: 0, Y: 0, Z: -1 } => ProbeHitFace.North,
            { X: 1, Y: 0, Z: 0 } => ProbeHitFace.East,
            { X: 0, Y: 0, Z: 1 } => ProbeHitFace.South,
            { X: -1, Y: 0, Z: 0 } => ProbeHitFace.West,
            { X: 0, Y: 1, Z: 0 } => ProbeHitFace.Up,
            { X: 0, Y: -1, Z: 0 } => ProbeHitFace.Down,
            _ => ProbeHitFace.Up,
        };
    }
}

internal readonly record struct LumOnWorldProbeTraceHit(
    double HitDistance,
    int HitBlockId,
    ProbeHitFace HitFace,
    VectorInt3 HitBlockPos,
    VectorInt3 HitFaceNormal,
    VectorInt3 SampleBlockPos,
    Vector4 SampleLightRgbS);

// TODO(WorldProbes): Consider extending hit metadata for future material/shading upgrades
// (e.g., liquid/transparent flags, block entity/material group, etc.).
