namespace VanillaGraphicsExpanded.LumOn.LumonScene;

internal readonly struct LumonSceneVirtualHandle
{
    public readonly ushort VirtualPageX;
    public readonly ushort VirtualPageY;

    public readonly byte VirtualSizePagesX;
    public readonly byte VirtualSizePagesY;

    public readonly ushort InTileOffsetX;
    public readonly ushort InTileOffsetY;

    public readonly ushort PatchSizeTexelsX;
    public readonly ushort PatchSizeTexelsY;

    public LumonSceneVirtualHandle(
        ushort virtualPageX,
        ushort virtualPageY,
        byte virtualSizePagesX,
        byte virtualSizePagesY,
        ushort inTileOffsetX,
        ushort inTileOffsetY,
        ushort patchSizeTexelsX,
        ushort patchSizeTexelsY)
    {
        VirtualPageX = virtualPageX;
        VirtualPageY = virtualPageY;
        VirtualSizePagesX = virtualSizePagesX;
        VirtualSizePagesY = virtualSizePagesY;
        InTileOffsetX = inTileOffsetX;
        InTileOffsetY = inTileOffsetY;
        PatchSizeTexelsX = patchSizeTexelsX;
        PatchSizeTexelsY = patchSizeTexelsY;
    }
}

