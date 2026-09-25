using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.LumOn.Scene.Geometry;

/// <summary>Mirrors capture prerequisites on the CPU without reading textures or confusing residency with publication.</summary>
internal sealed partial class TraceGeometryGpuScene
{
    private readonly long[] capturePublicationVersions;
    private TraceGeometryTables? publishedCaptureTables;

    #region Capture admission
    /// <summary>Provides an inexpensive dependency token; movement outside coverage does not expose aliased ring data.</summary>
    internal TraceGeometryCaptureStamp CaptureStamp(in TraceGeometryCapturePatch patch)
    {
        if (coverage?.Surface is not { } surface) return default;
        var bounds = coverage.Clip(surface);
        if (patch.Min.X < bounds.Min.X || patch.Min.Y < bounds.Min.Y || patch.Min.Z < bounds.Min.Z ||
            patch.Max.X > bounds.Max.X || patch.Max.Y > bounds.Max.Y || patch.Max.Z > bounds.Max.Z) return default;
        var coordinate = new PartitionCoordinate(patch.Min.X >> 4, patch.Min.Y >> 4, patch.Min.Z >> 4);
        return new(true, capturePublicationVersions[Slot(coordinate)], TablesRevision);
    }

    /// <summary>Checks the same geometry kinds, face-table readiness and surface IDs as GPU material capture.</summary>
    internal bool CanCapture(in TraceGeometryCapturePatch patch)
    {
        if (!CaptureStamp(patch).Covered || publishedCaptureTables == null) return false;
        var coordinate = new PartitionCoordinate(patch.Min.X >> 4, patch.Min.Y >> 4, patch.Min.Z >> 4);
        int slot = Slot(coordinate);
        if (owners[slot]?.Key.Coordinate != coordinate || geometryCells[slot].IsDefault) return false;
        var faces = publishedCaptureTables.Faces;
        for (int v = 0; v < 4; v++)
        for (int u = 0; u < 4; u++)
        {
            var cell = patch.Voxel(u, v);
            uint voxel = geometryCells[slot][((cell.Z & 15) << 8) + ((cell.Y & 15) << 4) + (cell.X & 15)];
            uint kind = voxel & 3;
            if (kind == 0) return false;
            if (kind == 1) continue;
            // Unsupported collision geometry is still capturable when its face material is published.
            uint material = voxel >> 2;
            int offset = checked((int)material << 2);
            if (material == 0 || offset < 0 || offset + 3 >= faces.Length || (faces[offset + 3] & 1) == 0) return false;
            uint surfaceId = (faces[offset + (int)(patch.Face >> 1)] >> ((int)(patch.Face & 1) << 4)) & 65535;
            if (surfaceId == 0) return false;
        }
        return true;
    }
    #endregion
}
