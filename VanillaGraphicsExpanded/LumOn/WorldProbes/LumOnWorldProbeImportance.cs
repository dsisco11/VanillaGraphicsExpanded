namespace VanillaGraphicsExpanded.LumOn.WorldProbes;

internal static class LumOnWorldProbeImportance
{
    public const float DirectSunlightFactor = 1f;
    public const float IndirectSunlightFactor = 2f;
    public const float StackedAboveRainMapFactor = 0.5f;
    public const float CardinalSolidNeighborBoost = 0.5f;
    public const LumOnWorldProbeImportanceFlags DynamicFlags =
        LumOnWorldProbeImportanceFlags.IndirectSunlight |
        LumOnWorldProbeImportanceFlags.StackedAboveRainMap;

    public static LumOnWorldProbeImportanceFlags GetDynamicFlags(
        int sunlight,
        int maximumSunlight,
        int localY,
        double probeY,
        double spacing,
        int rainMapHeight)
    {
        if (localY > 0
            && probeY >= rainMapHeight
            && probeY - spacing >= rainMapHeight)
        {
            return LumOnWorldProbeImportanceFlags.StackedAboveRainMap;
        }

        return sunlight == maximumSunlight
            ? LumOnWorldProbeImportanceFlags.None
            : LumOnWorldProbeImportanceFlags.IndirectSunlight;
    }

    public static float ComputeFactor(LumOnWorldProbeImportanceFlags flags)
    {
        float factor = (flags & LumOnWorldProbeImportanceFlags.StackedAboveRainMap) != 0
            ? StackedAboveRainMapFactor
            : (flags & LumOnWorldProbeImportanceFlags.IndirectSunlight) != 0
                ? IndirectSunlightFactor
                : DirectSunlightFactor;

        if ((flags & LumOnWorldProbeImportanceFlags.NearbySolidHit) != 0)
        {
            factor += CardinalSolidNeighborBoost;
        }

        return factor;
    }
}
