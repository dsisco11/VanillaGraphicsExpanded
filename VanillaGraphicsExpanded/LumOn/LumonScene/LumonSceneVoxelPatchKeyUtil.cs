using System;

namespace VanillaGraphicsExpanded.LumOn.LumonScene;

internal static class LumonSceneVoxelPatchKeyUtil
{
    public const int ChunkSizeVoxels = 32;
    public const int PatchSizeVoxels = 4;

    public static LumonScenePatchKey CreateForVoxelFace(
        LumonSceneFace face,
        int voxelX,
        int voxelY,
        int voxelZ)
    {
        if ((uint)voxelX >= ChunkSizeVoxels) throw new ArgumentOutOfRangeException(nameof(voxelX));
        if ((uint)voxelY >= ChunkSizeVoxels) throw new ArgumentOutOfRangeException(nameof(voxelY));
        if ((uint)voxelZ >= ChunkSizeVoxels) throw new ArgumentOutOfRangeException(nameof(voxelZ));

        byte plane;
        byte u;
        byte v;

        switch (face)
        {
            case LumonSceneFace.North:
            case LumonSceneFace.South:
                plane = (byte)voxelZ;
                u = (byte)(voxelX / PatchSizeVoxels);
                v = (byte)(voxelY / PatchSizeVoxels);
                break;

            case LumonSceneFace.East:
            case LumonSceneFace.West:
                plane = (byte)voxelX;
                u = (byte)(voxelZ / PatchSizeVoxels);
                v = (byte)(voxelY / PatchSizeVoxels);
                break;

            case LumonSceneFace.Up:
            case LumonSceneFace.Down:
                plane = (byte)voxelY;
                u = (byte)(voxelX / PatchSizeVoxels);
                v = (byte)(voxelZ / PatchSizeVoxels);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(face));
        }

        return LumonScenePatchKey.CreateVoxelFacePatch(face, planeIndex: plane, patchUIndex: u, patchVIndex: v);
    }
}
