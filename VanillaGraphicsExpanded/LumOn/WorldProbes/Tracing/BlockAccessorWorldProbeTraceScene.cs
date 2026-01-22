using System;
using System.Numerics;
using System.Threading;

using VanillaGraphicsExpanded.Numerics;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

internal sealed class BlockAccessorWorldProbeTraceScene : IWorldProbeTraceScene
{
    private readonly IBlockAccessor blockAccessor;

    private const double DirEpsilon = 1e-12;
    private const double PointInsideEpsilon = 1e-12;

    public BlockAccessorWorldProbeTraceScene(IBlockAccessor blockAccessor)
    {
        this.blockAccessor = blockAccessor ?? throw new ArgumentNullException(nameof(blockAccessor));
    }

    public bool Trace(Vector3d originWorld, Vector3 dirWorld, double maxDistance, CancellationToken cancellationToken, out LumOnWorldProbeTraceHit hit)
    {
        if (maxDistance <= 0)
        {
            hit = default;
            return false;
        }

        Vector3d dir = Vector3d.Normalize(Vector3d.FromVector3(dirWorld));
        if (dir.LengthSquared() < 1e-18)
        {
            hit = default;
            return false;
        }

        // Amanatides & Woo voxel traversal through 1x1x1 blocks.
        VectorInt3 voxelIndex = Vector3d.FloorToInt3(originWorld);
        int x = voxelIndex.X;
        int y = voxelIndex.Y;
        int z = voxelIndex.Z;

        double dx = dir.X;
        double dy = dir.Y;
        double dz = dir.Z;

        var step = new VectorInt3(
            dx >= 0 ? 1 : -1,
            dy >= 0 ? 1 : -1,
            dz >= 0 ? 1 : -1);

        // Vectorized initial tMax/tDelta.
        // - nextBoundary is the next voxel boundary in the ray direction for each axis.
        // - tMax is the t at which the ray crosses that boundary.
        // - tDelta is the t distance between successive crossings for that axis.
        var voxel = new Vector3d(x, y, z);
        var step01 = new Vector3d(step.X > 0 ? 1d : 0d, step.Y > 0 ? 1d : 0d, step.Z > 0 ? 1d : 0d);
        Vector3d nextBoundary = voxel + step01;

        Vector3d tMax = (nextBoundary - originWorld) / dir;
        Vector3d tDelta = Vector3d.Abs(Vector3d.One / dir);

        // Track which face we entered the current voxel through. For the first voxel,
        // this remains zero unless we step.
        var faceN = new VectorInt3(0, 0, 0);

        double t = 0.0;

        // World-probe tracing operates in the primary world dimension.
        var pos = new BlockPos(0);
        var samplePos = new BlockPos(0);

        while (t <= maxDistance)
        {
            cancellationToken.ThrowIfCancellationRequested();

            pos.Set(x, y, z);

            // Avoid forcing chunk loads; treat unloaded as miss.
            if (blockAccessor.GetChunkAtBlockPos(pos) == null)
            {
                hit = default;
                return false;
            }

            Block b = blockAccessor.GetMostSolidBlock(pos);
            if (b.Id != 0)
            {
                // Avoid querying collision boxes for air.
                Cuboidf[] boxes = b.GetCollisionBoxes(blockAccessor, pos);
                if (boxes != null && boxes.Length > 0)
                {
                    // Only treat as a hit if the ray actually intersects a collision box within the segment
                    // that lies inside the current voxel.
                    double tExit = Math.Min(tMax.X, Math.Min(tMax.Y, tMax.Z));
                    double tSegEnd = Math.Min(tExit, maxDistance);

                    if (TryIntersectCollisionBoxes(originWorld, dir, x, y, z, boxes, t, tSegEnd, faceN, dx, dy, dz, out double tHit, out VectorInt3 hitNormal))
                    {
                        int sx = x + hitNormal.X;
                        int sy = y + hitNormal.Y;
                        int sz = z + hitNormal.Z;

                        Vector4 light = Vector4.Zero;

                        samplePos.Set(sx, sy, sz);
                        if (blockAccessor.GetChunkAtBlockPos(samplePos) != null)
                        {
                            // Vec4f: XYZ = block light rgb, W = sun light brightness.
                            Vec4f ls = blockAccessor.GetLightRGBs(samplePos);
                            light = new Vector4(ls.X, ls.Y, ls.Z, ls.W);
                        }

                        hit = new LumOnWorldProbeTraceHit(
                            HitDistance: tHit,
                            HitBlockPos: new VectorInt3(x, y, z),
                            HitFaceNormal: hitNormal,
                            SampleBlockPos: new VectorInt3(sx, sy, sz),
                            SampleLightRgbS: light);
                        return true;
                    }
                }
            }

            // Advance to next voxel boundary.
            int axis = Vector3d.ArgMinAxis(tMax);
            switch (axis)
            {
                case 0:
                    x += step.X;
                    t = tMax.X;
                    tMax = tMax + new Vector3d(tDelta.X, 0d, 0d);
                    faceN = new VectorInt3(-step.X, 0, 0);
                    break;
                case 1:
                    y += step.Y;
                    t = tMax.Y;
                    tMax = tMax + new Vector3d(0d, tDelta.Y, 0d);
                    faceN = new VectorInt3(0, -step.Y, 0);
                    break;
                default:
                    z += step.Z;
                    t = tMax.Z;
                    tMax = tMax + new Vector3d(0d, 0d, tDelta.Z);
                    faceN = new VectorInt3(0, 0, -step.Z);
                    break;
            }
        }

        hit = default;
        return false;
    }

    private static bool TryIntersectCollisionBoxes(
        Vector3d originWorld,
        Vector3d dirWorld,
        int voxelX,
        int voxelY,
        int voxelZ,
        Cuboidf[] boxes,
        double tSegStart,
        double tSegEnd,
        VectorInt3 enteredFaceNormal,
        double dx,
        double dy,
        double dz,
        out double tHit,
        out VectorInt3 hitNormal)
    {
        tHit = default;
        hitNormal = default;

        if (tSegEnd < tSegStart)
        {
            return false;
        }

        // Point where we enter this voxel segment.
        Vector3d segOrigin = originWorld + (dirWorld * tSegStart);

        // Earliest hit across all collision boxes.
        double bestT = double.PositiveInfinity;
        VectorInt3 bestN = default;

        for (int i = 0; i < boxes.Length; i++)
        {
            Cuboidf box = boxes[i];

            // Convert to world-space AABB.
            double minX = voxelX + box.MinX;
            double minY = voxelY + box.MinY;
            double minZ = voxelZ + box.MinZ;
            double maxX = voxelX + box.MaxX;
            double maxY = voxelY + box.MaxY;
            double maxZ = voxelZ + box.MaxZ;

            // If we start inside the box for this voxel segment, treat as an immediate hit.
            if (
                segOrigin.X >= (minX - PointInsideEpsilon) && segOrigin.X <= (maxX + PointInsideEpsilon) &&
                segOrigin.Y >= (minY - PointInsideEpsilon) && segOrigin.Y <= (maxY + PointInsideEpsilon) &&
                segOrigin.Z >= (minZ - PointInsideEpsilon) && segOrigin.Z <= (maxZ + PointInsideEpsilon))
            {
                bestT = tSegStart;
                // If we arrived here by stepping into the voxel, the entered-face normal is the correct
                // "outside" direction to sample from (and it preserves stepping tie-break behavior).
                if (enteredFaceNormal.X != 0 || enteredFaceNormal.Y != 0 || enteredFaceNormal.Z != 0)
                {
                    bestN = enteredFaceNormal;
                }
                else
                {
                    bestN = ComputeInsideBoxFaceNormal(segOrigin, minX, minY, minZ, maxX, maxY, maxZ, dx, dy, dz);
                }
                break;
            }

            if (!TryRayAabbIntersection(originWorld, dirWorld, minX, minY, minZ, maxX, maxY, maxZ, tSegStart, tSegEnd, out double hitTLocal, out VectorInt3 hitNLocal))
            {
                continue;
            }

            // If we intersect right at the voxel-entry boundary, prefer the traversal-entered face normal.
            // This preserves the existing stepping tie-break behavior and is numerically more stable.
            if (
                (enteredFaceNormal.X != 0 || enteredFaceNormal.Y != 0 || enteredFaceNormal.Z != 0) &&
                Math.Abs(hitTLocal - tSegStart) <= 1e-12)
            {
                hitTLocal = tSegStart;
                hitNLocal = enteredFaceNormal;
            }

            if (hitTLocal < bestT)
            {
                bestT = hitTLocal;
                bestN = hitNLocal;
            }
        }

        if (double.IsInfinity(bestT))
        {
            return false;
        }

        tHit = bestT;
        hitNormal = bestN;
        return true;
    }

    private static bool TryRayAabbIntersection(
        Vector3d origin,
        Vector3d dir,
        double minX,
        double minY,
        double minZ,
        double maxX,
        double maxY,
        double maxZ,
        double tMin,
        double tMax,
        out double tHit,
        out VectorInt3 hitNormal)
    {
        tHit = default;
        hitNormal = default;

        double bestNear = tMin;
        int bestAxis = -1;
        int bestSign = 0;

        if (!IntersectAxis(origin.X, dir.X, minX, maxX, ref tMin, ref tMax, ref bestNear, ref bestAxis, ref bestSign, axis: 0))
        {
            return false;
        }

        if (!IntersectAxis(origin.Y, dir.Y, minY, maxY, ref tMin, ref tMax, ref bestNear, ref bestAxis, ref bestSign, axis: 1))
        {
            return false;
        }

        if (!IntersectAxis(origin.Z, dir.Z, minZ, maxZ, ref tMin, ref tMax, ref bestNear, ref bestAxis, ref bestSign, axis: 2))
        {
            return false;
        }

        if (bestAxis < 0)
        {
            // Segment overlaps the AABB but we didn't pick an entering plane (rare, usually due to tMin already clamping).
            // Fall back to a direction-based normal.
            Vector3d a = Vector3d.Abs(dir);
            if (a.X >= a.Y && a.X >= a.Z)
            {
                hitNormal = new VectorInt3(dir.X >= 0 ? -1 : 1, 0, 0);
            }
            else if (a.Y >= a.Z)
            {
                hitNormal = new VectorInt3(0, dir.Y >= 0 ? -1 : 1, 0);
            }
            else
            {
                hitNormal = new VectorInt3(0, 0, dir.Z >= 0 ? -1 : 1);
            }

            tHit = bestNear;
            return true;
        }

        tHit = bestNear;
        hitNormal = bestAxis switch
        {
            0 => new VectorInt3(bestSign, 0, 0),
            1 => new VectorInt3(0, bestSign, 0),
            _ => new VectorInt3(0, 0, bestSign),
        };

        return true;
    }

    private static bool IntersectAxis(
        double origin,
        double dir,
        double min,
        double max,
        ref double tMin,
        ref double tMax,
        ref double bestNear,
        ref int bestAxis,
        ref int bestSign,
        int axis)
    {
        if (Math.Abs(dir) < DirEpsilon)
        {
            // Parallel to this axis: must be within slab.
            return origin >= min && origin <= max;
        }

        double inv = 1.0 / dir;
        double t1 = (min - origin) * inv;
        double t2 = (max - origin) * inv;

        // Ensure t1 is near and t2 is far for this axis.
        int sign = dir > 0 ? -1 : 1;
        if (t1 > t2)
        {
            (t1, t2) = (t2, t1);
            sign = -sign;
        }

        // Update global near/far.
        if (t1 > tMin)
        {
            tMin = t1;
        }

        if (t2 < tMax)
        {
            tMax = t2;
        }

        if (tMin > tMax)
        {
            return false;
        }

        // Track the plane that produced the final near time.
        if (t1 >= bestNear)
        {
            bestNear = t1;
            bestAxis = axis;
            bestSign = sign;
        }

        return true;
    }

    private static VectorInt3 ComputeInsideBoxFaceNormal(
        Vector3d p,
        double minX,
        double minY,
        double minZ,
        double maxX,
        double maxY,
        double maxZ,
        double dx,
        double dy,
        double dz)
    {
        // Choose the nearest face to push the sample position out of the solid.
        // Tie-break (common when centered) using the dominant ray axis to preserve stable behavior.
        double dMinX = p.X - minX;
        double dMaxX = maxX - p.X;
        double dMinY = p.Y - minY;
        double dMaxY = maxY - p.Y;
        double dMinZ = p.Z - minZ;
        double dMaxZ = maxZ - p.Z;

        double best = dMinX;
        int axis = 0;
        int sign = -1;

        void Consider(double d, int a, int s)
        {
            if (d + 1e-15 < best)
            {
                best = d;
                axis = a;
                sign = s;
            }
        }

        Consider(dMaxX, 0, 1);
        Consider(dMinY, 1, -1);
        Consider(dMaxY, 1, 1);
        Consider(dMinZ, 2, -1);
        Consider(dMaxZ, 2, 1);

        // If tied between multiple nearest faces, fall back to dominant-axis direction (matches existing tests for centered origins).
        const double tieEpsilon = 1e-15;
        int ties = 0;
        if (Math.Abs(dMinX - best) < tieEpsilon) ties++;
        if (Math.Abs(dMaxX - best) < tieEpsilon) ties++;
        if (Math.Abs(dMinY - best) < tieEpsilon) ties++;
        if (Math.Abs(dMaxY - best) < tieEpsilon) ties++;
        if (Math.Abs(dMinZ - best) < tieEpsilon) ties++;
        if (Math.Abs(dMaxZ - best) < tieEpsilon) ties++;

        if (ties > 1)
        {
            Vector3d a = Vector3d.Abs(new Vector3d(dx, dy, dz));
            double dAx = a.X;
            double dAy = a.Y;
            double dAz = a.Z;

            if (dAx >= dAy && dAx >= dAz)
            {
                axis = 0;
                sign = dx >= 0 ? -1 : 1;
            }
            else if (dAy >= dAz)
            {
                axis = 1;
                sign = dy >= 0 ? -1 : 1;
            }
            else
            {
                axis = 2;
                sign = dz >= 0 ? -1 : 1;
            }
        }

        return axis switch
        {
            0 => new VectorInt3(sign, 0, 0),
            1 => new VectorInt3(0, sign, 0),
            _ => new VectorInt3(0, 0, sign),
        };
    }
}
