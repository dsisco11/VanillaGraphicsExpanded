using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

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
