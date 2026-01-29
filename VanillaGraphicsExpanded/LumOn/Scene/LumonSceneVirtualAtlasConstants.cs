namespace VanillaGraphicsExpanded.LumOn.Scene;

internal static class LumonSceneVirtualAtlasConstants
{
    public const int PhysicalAtlasSizeTexels = 4096;

    // Per-chunk virtual page table resolution. This is the virtual address space, not physical.
    // v1 assumes 1 tile per patch, so the max patches per chunk is VirtualPagesPerChunk.
    public const int VirtualPageTableWidth = 128;
    public const int VirtualPageTableHeight = 128;
    public const int VirtualPagesPerChunk = VirtualPageTableWidth * VirtualPageTableHeight; // 16384
}
