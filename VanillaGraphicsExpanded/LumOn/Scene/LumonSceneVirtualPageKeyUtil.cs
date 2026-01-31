namespace VanillaGraphicsExpanded.LumOn.Scene;

internal static class LumonSceneVirtualPageKeyUtil
{
    public static ulong Pack(uint chunkSlot, uint virtualPageIndex)
        => ((ulong)chunkSlot << 32) | virtualPageIndex;

    public static uint UnpackChunkSlot(ulong key)
        => (uint)(key >> 32);

    public static uint UnpackVirtualPageIndex(ulong key)
        => (uint)(key & 0xFFFF_FFFFu);
}

