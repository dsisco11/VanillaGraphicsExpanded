using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.LumOn.Scene.Geometry;

/// <summary>GPU-ready shared voxel in source X/Z/Y order; geometry is independent of material readiness.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct TraceGeometryVoxel(uint Geometry, uint LegacyLight, uint NormalizedLight)
{
    public const int Bytes = 12;
    public uint Kind => Geometry & 3;
    public uint Material => Geometry >> 2;

    /// <summary>Classifies exact opaque cubes independently of material availability; other occupied shapes remain unsupported.</summary>
    public static uint Classify(IBlockAccessor accessor, Block block, BlockPos position)
    {
        if (block.Id == 0) return 1;
        var boxes = block.GetCollisionBoxes(accessor, position);
        return block.RenderPass == EnumChunkRenderPass.Opaque && block.AllSidesOpaque && boxes is { Length: 1 } &&
            boxes[0].MinX == 0 && boxes[0].MinY == 0 && boxes[0].MinZ == 0 &&
            boxes[0].MaxX == 1 && boxes[0].MaxY == 1 && boxes[0].MaxZ == 1 ? 2u : 3u;
    }

    /// <summary>Quantizes the existing normalized light representation to four unsigned bytes.</summary>
    public static uint PackLight(in Vector4 light) => Byte(light.X) | Byte(light.Y) << 8 | Byte(light.Z) << 16 | Byte(light.W) << 24;

    /// <summary>Uses nearest normalized-byte quantization without allowing values outside the texture range.</summary>
    private static uint Byte(float value) => (uint)Math.Clamp((int)MathF.Round(value * 255), 0, 255);
}
