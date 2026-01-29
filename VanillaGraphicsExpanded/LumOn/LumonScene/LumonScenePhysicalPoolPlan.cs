namespace VanillaGraphicsExpanded.LumOn.LumonScene;

internal readonly struct LumonScenePhysicalPoolPlan
{
    public readonly LumonSceneField Field;
    public readonly int TileSizeTexels;
    public readonly int TilesPerAxis;
    public readonly int TilesPerAtlas;
    public readonly int RequestedPages;
    public readonly int CapacityPages;
    public readonly int AtlasCount;
    public readonly bool IsClampedByMaxAtlases;

    public LumonScenePhysicalPoolPlan(
        LumonSceneField field,
        int tileSizeTexels,
        int tilesPerAxis,
        int tilesPerAtlas,
        int requestedPages,
        int capacityPages,
        int atlasCount,
        bool isClampedByMaxAtlases)
    {
        Field = field;
        TileSizeTexels = tileSizeTexels;
        TilesPerAxis = tilesPerAxis;
        TilesPerAtlas = tilesPerAtlas;
        RequestedPages = requestedPages;
        CapacityPages = capacityPages;
        AtlasCount = atlasCount;
        IsClampedByMaxAtlases = isClampedByMaxAtlases;
    }
}

