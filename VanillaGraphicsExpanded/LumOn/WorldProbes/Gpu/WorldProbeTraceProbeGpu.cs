using System;
using System.Numerics;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;

/// <summary>One 64-byte probe record shared by every direction tile in its admission.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct WorldProbeTraceProbeGpu
{
    public int OriginX, OriginY, OriginZ, WorldHeight;
    public Vector4 FractionDistance;
    public uint OctahedralSize, FirstDirection, DirectionCount, MaxSteps;
    public Vector4 NearbyDistance;

    #region Construction
    /// <summary>Preserves integer-world precision while describing a bounded slice of selected texel indices.</summary>
    public WorldProbeTraceProbeGpu(Vector3d origin, double distance, int worldHeight,
        int size, int firstDirection, int directionCount, double nearbyDistance = 0, int maxSteps = 512)
    {
        this = default;
        if (!double.IsFinite(distance) || distance <= 0 || distance > float.MaxValue ||
            !double.IsFinite(origin.X) || !double.IsFinite(origin.Y) || !double.IsFinite(origin.Z) ||
            !double.IsFinite(nearbyDistance) || nearbyDistance < 0 || nearbyDistance > float.MaxValue ||
            size < 1 || size > 64 || firstDirection < 0 || directionCount < 1)
            throw new ArgumentOutOfRangeException(nameof(origin));
        OriginX = checked((int)Math.Floor(origin.X)); OriginY = checked((int)Math.Floor(origin.Y)); OriginZ = checked((int)Math.Floor(origin.Z));
        WorldHeight = worldHeight;
        FractionDistance = new((float)(origin.X-OriginX), (float)(origin.Y-OriginY), (float)(origin.Z-OriginZ), (float)distance);
        OctahedralSize = (uint)size; FirstDirection = (uint)firstDirection; DirectionCount = (uint)directionCount;
        MaxSteps = (uint)Math.Clamp(maxSteps, 0, 512); NearbyDistance = new((float)nearbyDistance, 0, 0, 0);
    }
    #endregion
}
