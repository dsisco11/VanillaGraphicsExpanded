using System;
using System.Collections.Immutable;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.LumOn.Scene.Geometry;

/// <summary>Exposes exact published capture inputs without GPU readback or a second source capture.</summary>
internal sealed partial class TraceGeometryGpuScene
{
    private readonly ImmutableArray<uint>[] geometryCells;

    /// <summary>Reports bounded CPU storage retained for identity comparisons; lighting bytes are not retained.</summary>
    internal long CaptureIdentityBytes
    {
        get
        {
            long bytes = 0;
            foreach (var cell in geometryCells) bytes += cell.IsDefault ? 0 : (long)cell.Length << 2;
            return bytes;
        }
    }

    #region Capture identity reads
    /// <summary>Copies the sixteen owning voxels of a four-by-four face patch in capture order.</summary>
    internal bool TryReadCaptureIdentity(VectorInt3 chunk, uint patchId, Span<uint> identity)
    {
        if (identity.Length != 16 || !TraceGeometryCapturePatch.TryCreate(chunk, patchId, out var patch) ||
            !CaptureStamp(patch).Covered) return false;
        for (int v = 0; v < 4; v++)
        for (int u = 0; u < 4; u++)
        {
            var voxelPosition = patch.Voxel(u, v);
            int x = voxelPosition.X, y = voxelPosition.Y, z = voxelPosition.Z;
            var coordinate = new PartitionCoordinate(x >> 4, y >> 4, z >> 4);
            int slot = Slot(coordinate);
            // A dirty notification withdraws readiness but does not prove an identity change.
            // Retained page inputs can be compared after this cell is coherently republished.
            if (owners[slot]?.Key.Coordinate != coordinate || geometryCells[slot].IsDefault) return false;
            uint voxel = geometryCells[slot][((z & 15) << 8) + ((y & 15) << 4) + (x & 15)];
            if ((voxel & 3) == 0) return false;
            identity[(v << 2) + u] = voxel;
        }
        // Face/material tables are immutable within a scene generation. The geometry word
        // therefore identifies both occupancy classification and the owning block's material.
        return true;
    }
    #endregion
}
