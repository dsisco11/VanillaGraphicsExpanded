using Vintagestory.API.Client;
using System.Numerics;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.LumOn.Scene.LocalTracing;

/// <summary>Captures supported geometry and normalized engine light without guessing unsupported block shapes.</summary>
internal static class LocalTraceCellCapture
{
    #region Snapshot Capture
    /// <summary>Captures a full opaque cube, air, or an explicitly unavailable geometry cell.</summary>
    public static LocalTraceSourceCell Capture(IBlockAccessor accessor, Block block, BlockPos position, LocalTraceMaterialRegistry materials)
    {
        var light = accessor.GetLightRGBs(position);
        var normalized = Vector4.Clamp(new Vector4(light.X, light.Y, light.Z, light.W), Vector4.Zero, Vector4.One);
        // Bulk solid-layer snapshots may omit fluid-layer geometry.
        if (block.Id == 0) block = accessor.GetMostSolidBlock(position);
        if (block.Id == 0) return new LocalTraceSourceCell(1, normalized);
        var boxes = block.GetCollisionBoxes(accessor, position);
        // Conservative representation: decorative, partial and transmissive geometry is
        // unresolved until a faithful representation is supplied, never guessed as air.
        if (block.RenderPass != EnumChunkRenderPass.Opaque || !block.AllSidesOpaque || boxes is null || boxes.Length != 1) return new LocalTraceSourceCell(0, normalized);
        var b = boxes[0];
        if (b.MinX != 0 || b.MinY != 0 || b.MinZ != 0 || b.MaxX != 1 || b.MaxY != 1 || b.MaxZ != 1)
            return new LocalTraceSourceCell(0, normalized);
        uint material = materials.Resolve(block);
        // Geometry remains opaque even if material resolution is unavailable.
        return new LocalTraceSourceCell(2u | (material << 2), normalized);
    }
    #endregion
}
