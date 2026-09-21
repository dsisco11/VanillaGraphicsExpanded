using Vintagestory.API.Client;
using System.Numerics;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.LumOn.Scene.NearField;

/// <summary>Captures supported geometry and normalized engine light without guessing unsupported block shapes.</summary>
internal static class NearFieldCellCapture
{
    #region Snapshot Capture
    /// <summary>Captures a full opaque cube, air, or an explicitly unavailable geometry cell.</summary>
    public static NearFieldSourceCell Capture(IBlockAccessor accessor, Block block, BlockPos position, NearFieldMaterialRegistry materials)
    {
        var light = accessor.GetLightRGBs(position);
        var normalized = Vector4.Clamp(new Vector4(light.X, light.Y, light.Z, light.W), Vector4.Zero, Vector4.One);
        // Bulk solid-layer snapshots may omit fluid-layer geometry.
        if (block.Id == 0) block = accessor.GetMostSolidBlock(position);
        return CaptureGeometry(accessor, block, position, materials, normalized);
    }

    /// <summary>Evaluates geometry against captured lighting, retaining position-dependent collision behavior.</summary>
    public static NearFieldSourceCell CaptureGeometry(IBlockAccessor accessor, Block block, BlockPos position,
        NearFieldMaterialRegistry materials, in Vector4 normalized, Dictionary<int, uint>? materialIndices = null)
    {
        if (block.Id == 0) return new NearFieldSourceCell(1, normalized);
        var boxes = block.GetCollisionBoxes(accessor, position);
        // Conservative representation: decorative, partial and transmissive geometry is
        // unresolved until a faithful representation is supplied, never guessed as air.
        if (block.RenderPass != EnumChunkRenderPass.Opaque || !block.AllSidesOpaque || boxes is null || boxes.Length != 1) return new NearFieldSourceCell(0, normalized);
        var b = boxes[0];
        if (b.MinX != 0 || b.MinY != 0 || b.MinZ != 0 || b.MaxX != 1 || b.MaxY != 1 || b.MaxZ != 1)
            return new NearFieldSourceCell(0, normalized);
        uint material;
        if (materialIndices == null || !materialIndices.TryGetValue(block.Id, out material))
        {
            material = materials.Resolve(block);
            if (materialIndices != null) materialIndices[block.Id] = material;
        }
        // Geometry remains opaque even if material resolution is unavailable.
        return new NearFieldSourceCell(2u | (material << 2), normalized);
    }
    #endregion
}
