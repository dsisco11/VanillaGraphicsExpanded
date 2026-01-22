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
        int x = (int)Math.Floor(originWorld.X);
        int y = (int)Math.Floor(originWorld.Y);
        int z = (int)Math.Floor(originWorld.Z);

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
                    // If we hit inside the first voxel, synthesize a reasonable face normal based on ray direction.
                    // This ensures the "adjacent sample voxel" is outside the hit voxel.
                    if (faceN.X == 0 && faceN.Y == 0 && faceN.Z == 0)
                    {
                        Vector3d a = Vector3d.Abs(dir);
                        double ax = a.X;
                        double ay = a.Y;
                        double az = a.Z;

                        if (ax >= ay && ax >= az)
                        {
                            faceN = new VectorInt3(dx >= 0 ? -1 : 1, 0, 0);
                        }
                        else if (ay >= az)
                        {
                            faceN = new VectorInt3(0, dy >= 0 ? -1 : 1, 0);
                        }
                        else
                        {
                            faceN = new VectorInt3(0, 0, dz >= 0 ? -1 : 1);
                        }
                    }

                    int sx = x + faceN.X;
                    int sy = y + faceN.Y;
                    int sz = z + faceN.Z;

                    Vector4 light = Vector4.Zero;

                    samplePos.Set(sx, sy, sz);
                    if (blockAccessor.GetChunkAtBlockPos(samplePos) != null)
                    {
                        // Vec4f: XYZ = block light rgb, W = sun light brightness.
                        Vec4f ls = blockAccessor.GetLightRGBs(samplePos);
                        light = new Vector4(ls.X, ls.Y, ls.Z, ls.W);
                    }

                    hit = new LumOnWorldProbeTraceHit(
                        HitDistance: t,
                        HitBlockPos: new VectorInt3(x, y, z),
                        HitFaceNormal: faceN,
                        SampleBlockPos: new VectorInt3(sx, sy, sz),
                        SampleLightRgbS: light);
                    return true;
                }
            }

            // Advance to next voxel boundary.
            if (tMax.X < tMax.Y)
            {
                if (tMax.X < tMax.Z)
                {
                    x += step.X;
                    t = tMax.X;
                    tMax = tMax + new Vector3d(tDelta.X, 0d, 0d);
                    faceN = new VectorInt3(-step.X, 0, 0);
                }
                else
                {
                    z += step.Z;
                    t = tMax.Z;
                    tMax = tMax + new Vector3d(0d, 0d, tDelta.Z);
                    faceN = new VectorInt3(0, 0, -step.Z);
                }
            }
            else
            {
                if (tMax.Y < tMax.Z)
                {
                    y += step.Y;
                    t = tMax.Y;
                    tMax = tMax + new Vector3d(0d, tDelta.Y, 0d);
                    faceN = new VectorInt3(0, -step.Y, 0);
                }
                else
                {
                    z += step.Z;
                    t = tMax.Z;
                    tMax = tMax + new Vector3d(0d, 0d, tDelta.Z);
                    faceN = new VectorInt3(0, 0, -step.Z);
                }
            }
        }

        hit = default;
        return false;
    }
}
