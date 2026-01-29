using System;

namespace VanillaGraphicsExpanded.LumOn.Scene;

internal readonly struct LumonSceneVoxelPatchLayoutDesc
{
    public readonly int TexelsPerVoxelFaceEdge;
    public readonly int PatchSizeTexels;
    public readonly int TileSizeTexels;
    public readonly int InTileOffsetTexels;
    public readonly int BorderTexels;

    public LumonSceneVoxelPatchLayoutDesc(int texelsPerVoxelFaceEdge, int patchSizeTexels, int inTileOffsetTexels)
    {
        TexelsPerVoxelFaceEdge = texelsPerVoxelFaceEdge;
        PatchSizeTexels = patchSizeTexels;
        TileSizeTexels = patchSizeTexels;
        InTileOffsetTexels = inTileOffsetTexels;
        BorderTexels = inTileOffsetTexels;
    }
}

internal static class LumonSceneVoxelPatchLayout
{
    public const int VoxelsPerPatchEdge = 4;

    public static readonly LumonSceneVoxelPatchLayoutDesc DefaultNear = Create(texelsPerVoxelFaceEdge: 4);
    public static readonly LumonSceneVoxelPatchLayoutDesc DefaultFar = Create(texelsPerVoxelFaceEdge: 1);

    public static LumonSceneVoxelPatchLayoutDesc Create(int texelsPerVoxelFaceEdge)
    {
        if (texelsPerVoxelFaceEdge <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(texelsPerVoxelFaceEdge));
        }

        int patchSize = VoxelsPerPatchEdge * texelsPerVoxelFaceEdge;
        if (patchSize > LumonSceneVirtualAtlasConstants.PhysicalAtlasSizeTexels)
        {
            throw new ArgumentOutOfRangeException(nameof(texelsPerVoxelFaceEdge), $"Patch size {patchSize} exceeds physical atlas size {LumonSceneVirtualAtlasConstants.PhysicalAtlasSizeTexels}.");
        }

        // v1: tile size equals patch size (Resolution x PatchSizeVoxels).
        int offset = 0;
        return new LumonSceneVoxelPatchLayoutDesc(texelsPerVoxelFaceEdge, patchSize, offset);
    }
}
