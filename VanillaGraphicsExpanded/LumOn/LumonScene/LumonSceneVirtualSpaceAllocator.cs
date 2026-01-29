using System;

namespace VanillaGraphicsExpanded.LumOn.LumonScene;

/// <summary>
/// Per-chunk virtual address allocator for patch virtual tiles.
/// v1 policy: each patch maps to exactly one virtual page, deterministically derived from PatchId.
/// </summary>
internal sealed class LumonSceneVirtualSpaceAllocator
{
    public bool TryAllocateOnePage(LumonScenePatchId patchId, out ushort virtualPageX, out ushort virtualPageY)
    {
        int id = patchId.Value;
        if ((uint)id >= (uint)LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk)
        {
            virtualPageX = 0;
            virtualPageY = 0;
            return false;
        }

        int x = id & (LumonSceneVirtualAtlasConstants.VirtualPageTableWidth - 1);
        int y = id >> 7; // log2(128)
        virtualPageX = (ushort)x;
        virtualPageY = (ushort)y;
        return true;
    }
}

