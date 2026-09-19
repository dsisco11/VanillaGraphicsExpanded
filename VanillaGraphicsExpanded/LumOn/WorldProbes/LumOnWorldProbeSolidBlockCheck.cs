using System;

using VanillaGraphicsExpanded.Numerics;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes;

internal static class LumOnWorldProbeSolidBlockCheck
{
    public static bool IsProbeCenterInsideSolidBlock(IBlockAccessor blockAccessor, Vector3d probePosWorld)
    {
        return ClassifyProbeCenter(blockAccessor, probePosWorld) == LumOnWorldProbeCenterOccupancy.InsideCollision;
    }

    public static LumOnWorldProbeCenterOccupancy ClassifyProbeCenter(IBlockAccessor blockAccessor, Vector3d probePosWorld)
    {
        if (blockAccessor is null)
        {
            return LumOnWorldProbeCenterOccupancy.Unavailable;
        }

        try
        {
            var pos = new BlockPos(0);
            pos.Set((int)Math.Floor(probePosWorld.X), (int)Math.Floor(probePosWorld.Y), (int)Math.Floor(probePosWorld.Z));

            // The primary world occupies [0, MapSizeY) in block coordinates.
            // Outside this range the accessor returns air, which would otherwise trace as open sky.
            int mapSizeY = blockAccessor.MapSizeY;
            if (mapSizeY > 0 && (pos.Y < 0 || pos.Y >= mapSizeY))
            {
                return LumOnWorldProbeCenterOccupancy.OutsideWorldHeight;
            }

            // Avoid forcing chunk loads; if it's not loaded, don't permanently disable.
            if (blockAccessor.GetChunkAtBlockPos(pos) == null)
            {
                return LumOnWorldProbeCenterOccupancy.Unavailable;
            }

            Block b = blockAccessor.GetMostSolidBlock(pos);
            if (b.Id == 0)
            {
                return LumOnWorldProbeCenterOccupancy.Empty;
            }

            Cuboidf[] boxes = b.GetCollisionBoxes(blockAccessor, pos);
            if (boxes is null || boxes.Length == 0)
            {
                return LumOnWorldProbeCenterOccupancy.NoCollision;
            }

            float lx = (float)(probePosWorld.X - pos.X);
            float ly = (float)(probePosWorld.Y - pos.Y);
            float lz = (float)(probePosWorld.Z - pos.Z);

            const float eps = 1e-4f;
            for (int i = 0; i < boxes.Length; i++)
            {
                var c = boxes[i];
                if (lx >= c.X1 - eps && lx <= c.X2 + eps &&
                    ly >= c.Y1 - eps && ly <= c.Y2 + eps &&
                    lz >= c.Z1 - eps && lz <= c.Z2 + eps)
                {
                    return LumOnWorldProbeCenterOccupancy.InsideCollision;
                }
            }

            return LumOnWorldProbeCenterOccupancy.OutsideCollision;
        }
        catch (NotImplementedException)
        {
            return LumOnWorldProbeCenterOccupancy.Unavailable;
        }
    }
}
