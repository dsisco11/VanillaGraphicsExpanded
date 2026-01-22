using System;
using System.Threading;

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

    public bool Trace(Vec3d originWorld, Vec3f dirWorld, double maxDistance, CancellationToken cancellationToken, out LumOnWorldProbeTraceHit hit)
    {
        ArgumentNullException.ThrowIfNull(originWorld);

        if (maxDistance <= 0)
        {
            hit = default;
            return false;
        }

        double dx = dirWorld.X;
        double dy = dirWorld.Y;
        double dz = dirWorld.Z;

        double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (len < 1e-9)
        {
            hit = default;
            return false;
        }

        dx /= len;
        dy /= len;
        dz /= len;

        // Amanatides & Woo voxel traversal through 1x1x1 blocks.
        int x = (int)Math.Floor(originWorld.X);
        int y = (int)Math.Floor(originWorld.Y);
        int z = (int)Math.Floor(originWorld.Z);

        int stepX = dx >= 0 ? 1 : -1;
        int stepY = dy >= 0 ? 1 : -1;
        int stepZ = dz >= 0 ? 1 : -1;

        double nextVoxBoundaryX = x + (dx >= 0 ? 1.0 : 0.0);
        double nextVoxBoundaryY = y + (dy >= 0 ? 1.0 : 0.0);
        double nextVoxBoundaryZ = z + (dz >= 0 ? 1.0 : 0.0);

        double tMaxX = dx == 0 ? double.PositiveInfinity : (nextVoxBoundaryX - originWorld.X) / dx;
        double tMaxY = dy == 0 ? double.PositiveInfinity : (nextVoxBoundaryY - originWorld.Y) / dy;
        double tMaxZ = dz == 0 ? double.PositiveInfinity : (nextVoxBoundaryZ - originWorld.Z) / dz;

        double tDeltaX = dx == 0 ? double.PositiveInfinity : Math.Abs(1.0 / dx);
        double tDeltaY = dy == 0 ? double.PositiveInfinity : Math.Abs(1.0 / dy);
        double tDeltaZ = dz == 0 ? double.PositiveInfinity : Math.Abs(1.0 / dz);

        // Track which face we entered the current voxel through. For the first voxel,
        // this remains zero unless we step.
        int faceNx = 0;
        int faceNy = 0;
        int faceNz = 0;

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
                    if (faceNx == 0 && faceNy == 0 && faceNz == 0)
                    {
                        double ax = Math.Abs(dx);
                        double ay = Math.Abs(dy);
                        double az = Math.Abs(dz);

                        if (ax >= ay && ax >= az)
                        {
                            faceNx = dx >= 0 ? -1 : 1;
                        }
                        else if (ay >= az)
                        {
                            faceNy = dy >= 0 ? -1 : 1;
                        }
                        else
                        {
                            faceNz = dz >= 0 ? -1 : 1;
                        }
                    }

                    int sx = x + faceNx;
                    int sy = y + faceNy;
                    int sz = z + faceNz;

                    Vec4f light = new Vec4f();

                    samplePos.Set(sx, sy, sz);
                    if (blockAccessor.GetChunkAtBlockPos(samplePos) != null)
                    {
                        // Vec4f: XYZ = block light rgb, W = sun light brightness.
                        light = blockAccessor.GetLightRGBs(samplePos);
                    }

                    hit = new LumOnWorldProbeTraceHit(
                        HitDistance: t,
                        HitBlockPos: new Vec3i(x, y, z),
                        HitFaceNormal: new Vec3i(faceNx, faceNy, faceNz),
                        SampleBlockPos: new Vec3i(sx, sy, sz),
                        SampleLightRgbS: light);
                    return true;
                }
            }

            // Advance to next voxel boundary.
            if (tMaxX < tMaxY)
            {
                if (tMaxX < tMaxZ)
                {
                    x += stepX;
                    t = tMaxX;
                    tMaxX += tDeltaX;
                    faceNx = -stepX;
                    faceNy = 0;
                    faceNz = 0;
                }
                else
                {
                    z += stepZ;
                    t = tMaxZ;
                    tMaxZ += tDeltaZ;
                    faceNx = 0;
                    faceNy = 0;
                    faceNz = -stepZ;
                }
            }
            else
            {
                if (tMaxY < tMaxZ)
                {
                    y += stepY;
                    t = tMaxY;
                    tMaxY += tDeltaY;
                    faceNx = 0;
                    faceNy = -stepY;
                    faceNz = 0;
                }
                else
                {
                    z += stepZ;
                    t = tMaxZ;
                    tMaxZ += tDeltaZ;
                    faceNx = 0;
                    faceNy = 0;
                    faceNz = -stepZ;
                }
            }
        }

        hit = default;
        return false;
    }
}
