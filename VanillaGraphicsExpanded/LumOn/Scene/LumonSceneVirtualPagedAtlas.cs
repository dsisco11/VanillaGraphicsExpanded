using System;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>
/// Per-chunk virtual atlas that maps PatchIds to virtual tiles and in-tile patch rects.
/// This is virtual address space only; physical tile residency is handled by the global pool + page tables.
/// </summary>
internal sealed class LumonSceneVirtualPagedAtlas
{
    private readonly LumonSceneVirtualSpaceAllocator allocator = new();

    public bool TryGetVoxelPatchHandle(LumonScenePatchId patchId, out LumonSceneVirtualHandle handle)
        => TryGetVoxelPatchHandle(patchId, LumonSceneVoxelPatchLayout.DefaultNear, out handle);

    public bool TryGetVoxelPatchHandle(LumonScenePatchId patchId, in LumonSceneVoxelPatchLayoutDesc layout, out LumonSceneVirtualHandle handle)
    {
        if (!allocator.TryAllocateOnePage(patchId, out ushort vx, out ushort vy))
        {
            handle = default;
            return false;
        }

        handle = new LumonSceneVirtualHandle(
            virtualPageX: vx,
            virtualPageY: vy,
            virtualSizePagesX: 1,
            virtualSizePagesY: 1,
            inTileOffsetX: (ushort)layout.InTileOffsetTexels,
            inTileOffsetY: (ushort)layout.InTileOffsetTexels,
            patchSizeTexelsX: (ushort)layout.PatchSizeTexels,
            patchSizeTexelsY: (ushort)layout.PatchSizeTexels);

        return true;
    }
}
