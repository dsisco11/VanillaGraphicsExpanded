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

    public bool Trace(Vec3d originWorld, Vec3f dirWorld, double maxDistance, CancellationToken cancellationToken, out double hitDistance)
    {
        ArgumentNullException.ThrowIfNull(originWorld);

        if (maxDistance <= 0)
        {
            hitDistance = 0;
            return false;
        }

        double dx = dirWorld.X;
        double dy = dirWorld.Y;
        double dz = dirWorld.Z;

        double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (len < 1e-9)
        {
            hitDistance = 0;
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

        // Skip the starting cell if origin is inside solid.
        double t = 0.0;

        while (t <= maxDistance)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pos = new BlockPos(x, y, z);

            // Avoid forcing chunk loads; treat unloaded as miss.
            if (blockAccessor.GetChunkAtBlockPos(pos) == null)
            {
                hitDistance = 0;
                return false;
            }

            Block b = blockAccessor.GetMostSolidBlock(pos);
            Cuboidf[] boxes = b.GetCollisionBoxes(blockAccessor, pos);

            if (b.Id != 0 && boxes != null && boxes.Length > 0)
            {
                hitDistance = t;
                return true;
            }

            // Advance to next voxel boundary.
            if (tMaxX < tMaxY)
            {
                if (tMaxX < tMaxZ)
                {
                    x += stepX;
                    t = tMaxX;
                    tMaxX += tDeltaX;
                }
                else
                {
                    z += stepZ;
                    t = tMaxZ;
                    tMaxZ += tDeltaZ;
                }
            }
            else
            {
                if (tMaxY < tMaxZ)
                {
                    y += stepY;
                    t = tMaxY;
                    tMaxY += tDeltaY;
                }
                else
                {
                    z += stepZ;
                    t = tMaxZ;
                    tMaxZ += tDeltaZ;
                }
            }
        }

        hitDistance = 0;
        return false;
    }
}
