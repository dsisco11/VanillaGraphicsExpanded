using System;

using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes;

internal static class LumOnClipmapTopology
{
    public static double GetSpacing(double baseSpacing, int level)
    {
        if (baseSpacing <= 0) throw new ArgumentOutOfRangeException(nameof(baseSpacing));
        if (level < 0) throw new ArgumentOutOfRangeException(nameof(level));

        return baseSpacing * (1 << level);
    }

    public static Vec3d SnapAnchor(Vec3d cameraPos, double spacing)
    {
        ArgumentNullException.ThrowIfNull(cameraPos);
        if (spacing <= 0) throw new ArgumentOutOfRangeException(nameof(spacing));

        return new Vec3d(
            Math.Floor(cameraPos.X / spacing) * spacing,
            Math.Floor(cameraPos.Y / spacing) * spacing,
            Math.Floor(cameraPos.Z / spacing) * spacing);
    }

    public static Vec3d GetOriginMinCorner(Vec3d anchor, double spacing, int resolution)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        if (spacing <= 0) throw new ArgumentOutOfRangeException(nameof(spacing));
        if (resolution <= 0) throw new ArgumentOutOfRangeException(nameof(resolution));

        int half = resolution / 2;
        return new Vec3d(
            anchor.X - half * spacing,
            anchor.Y - half * spacing,
            anchor.Z - half * spacing);
    }

    public static Vec3d WorldToLocal(Vec3d pos, Vec3d originMinCorner, double spacing)
    {
        ArgumentNullException.ThrowIfNull(pos);
        ArgumentNullException.ThrowIfNull(originMinCorner);
        if (spacing <= 0) throw new ArgumentOutOfRangeException(nameof(spacing));

        return new Vec3d(
            (pos.X - originMinCorner.X) / spacing,
            (pos.Y - originMinCorner.Y) / spacing,
            (pos.Z - originMinCorner.Z) / spacing);
    }

    public static Vec3i LocalToIndexFloor(Vec3d local)
    {
        ArgumentNullException.ThrowIfNull(local);

        return new Vec3i(
            (int)Math.Floor(local.X),
            (int)Math.Floor(local.Y),
            (int)Math.Floor(local.Z));
    }

    public static Vec3d LocalToFrac(Vec3d local, Vec3i indexFloor)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(indexFloor);

        return new Vec3d(
            local.X - indexFloor.X,
            local.Y - indexFloor.Y,
            local.Z - indexFloor.Z);
    }

    public static bool IsIndexInBounds(Vec3i index, int resolution)
    {
        ArgumentNullException.ThrowIfNull(index);
        if (resolution <= 0) throw new ArgumentOutOfRangeException(nameof(resolution));

        return (uint)index.X < (uint)resolution &&
               (uint)index.Y < (uint)resolution &&
               (uint)index.Z < (uint)resolution;
    }

    public static int WrapIndex(int index, int resolution)
    {
        if (resolution <= 0) throw new ArgumentOutOfRangeException(nameof(resolution));
        return GameMath.Mod(index, resolution);
    }

    public static Vec3i WrapIndex(Vec3i index, int resolution)
    {
        ArgumentNullException.ThrowIfNull(index);
        if (resolution <= 0) throw new ArgumentOutOfRangeException(nameof(resolution));

        return new Vec3i(
            GameMath.Mod(index.X, resolution),
            GameMath.Mod(index.Y, resolution),
            GameMath.Mod(index.Z, resolution));
    }

    public static int LinearIndex(Vec3i index, int resolution)
    {
        ArgumentNullException.ThrowIfNull(index);
        if (resolution <= 0) throw new ArgumentOutOfRangeException(nameof(resolution));

        return index.X + (index.Y * resolution) + (index.Z * resolution * resolution);
    }

    public static Vec3d IndexToProbeCenterWorld(Vec3i index, Vec3d originMinCorner, double spacing)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(originMinCorner);
        if (spacing <= 0) throw new ArgumentOutOfRangeException(nameof(spacing));

        return new Vec3d(
            originMinCorner.X + (index.X + 0.5) * spacing,
            originMinCorner.Y + (index.Y + 0.5) * spacing,
            originMinCorner.Z + (index.Z + 0.5) * spacing);
    }

    public static int SelectLevelByDistance(Vec3d pos, Vec3d cameraPos, double baseSpacing, int maxLevel)
    {
        ArgumentNullException.ThrowIfNull(pos);
        ArgumentNullException.ThrowIfNull(cameraPos);
        if (baseSpacing <= 0) throw new ArgumentOutOfRangeException(nameof(baseSpacing));
        if (maxLevel < 0) throw new ArgumentOutOfRangeException(nameof(maxLevel));

        double dx = pos.X - cameraPos.X;
        double dy = pos.Y - cameraPos.Y;
        double dz = pos.Z - cameraPos.Z;
        double dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);

        double ratio = Math.Max(dist, baseSpacing) / baseSpacing;
        int level = (int)Math.Floor(Math.Log(ratio, 2));
        return Math.Clamp(level, 0, maxLevel);
    }

    public static double DistanceToBoundaryProbeUnits(Vec3d local, int resolution)
    {
        ArgumentNullException.ThrowIfNull(local);
        if (resolution <= 1) throw new ArgumentOutOfRangeException(nameof(resolution));

        double max = resolution - 1;

        double dx = Math.Min(local.X, max - local.X);
        double dy = Math.Min(local.Y, max - local.Y);
        double dz = Math.Min(local.Z, max - local.Z);

        return Math.Min(dx, Math.Min(dy, dz));
    }

    public static double ComputeCrossLevelBlendWeight(double edgeDistProbeUnits, double blendStartProbeUnits, double blendWidthProbeUnits)
    {
        if (blendWidthProbeUnits <= 0) throw new ArgumentOutOfRangeException(nameof(blendWidthProbeUnits));

        // 0 near the boundary -> favor L+1.
        // 1 deeper inside level -> favor L.
        double t = (edgeDistProbeUnits - blendStartProbeUnits) / blendWidthProbeUnits;
        return Math.Clamp(t, 0.0, 1.0);
    }
}
