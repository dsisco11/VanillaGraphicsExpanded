namespace VanillaGraphicsExpanded.LumOn.Scene;

internal readonly struct LumonSceneFieldChunkBudget
{
    public readonly int RadiusChunks;
    public readonly int SideChunks;
    public readonly int CoveredChunks;
    public readonly int ExtraChunks;
    public readonly int TotalChunks;

    public LumonSceneFieldChunkBudget(int radiusChunks, int sideChunks, int coveredChunks, int extraChunks, int totalChunks)
    {
        RadiusChunks = radiusChunks;
        SideChunks = sideChunks;
        CoveredChunks = coveredChunks;
        ExtraChunks = extraChunks;
        TotalChunks = totalChunks;
    }
}

