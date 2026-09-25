using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.LumOn.Scene.Geometry;

/// <summary>Exact owning voxels of an aligned four-by-four surface patch, all within one publication cell.</summary>
internal readonly record struct TraceGeometryCapturePatch(VectorInt3 Min, VectorInt3 Max, uint Axis)
{
    public uint Face => Axis switch { 0 => 1, 1 => 3, 2 => 4, 3 => 5, 4 => 2, _ => 0 };

    #region Patch decoding
    /// <summary>Decodes integer source bounds without using floating camera or surface offsets.</summary>
    public static bool TryCreate(VectorInt3 chunk, uint patchId, out TraceGeometryCapturePatch patch)
    {
        patch = default;
        if (patchId == 0 || patchId > 12288) return false;
        uint linear = (patchId - 1) / 6, axis = (patchId - 1) % 6;
        int plane = (int)(linear >> 6), u = (int)(linear & 7) << 2, v = (int)((linear >> 3) & 7) << 2;
        var min = new VectorInt3((chunk.X << 5) + (axis < 2 ? plane : u),
            (chunk.Y << 5) + (axis < 2 ? v : axis < 4 ? plane : v),
            (chunk.Z << 5) + (axis < 2 ? u : axis < 4 ? v : plane));
        patch = new(min, new(min.X + (axis < 2 ? 1 : 4), min.Y + (axis >= 2 && axis < 4 ? 1 : 4),
            min.Z + (axis >= 4 ? 1 : 4)), axis);
        return true;
    }

    /// <summary>Preserves the capture identity's V-major source ordering for every face orientation.</summary>
    public VectorInt3 Voxel(int u, int v) => new(Min.X + (Axis < 2 ? 0 : u),
        Min.Y + (Axis < 2 || Axis >= 4 ? v : 0), Min.Z + (Axis < 2 ? u : Axis < 4 ? v : 0));
    #endregion
}
