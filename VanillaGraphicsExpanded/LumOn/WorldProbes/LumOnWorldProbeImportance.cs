namespace VanillaGraphicsExpanded.LumOn.WorldProbes;

internal static class LumOnWorldProbeImportance
{
    public const float DirectSunlightFactor = 1f;
    public const float IndirectSunlightFactor = 2f;
    public const float StackedAboveRainMapFactor = 0.5f;

    public static float GetFactor(
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
            return StackedAboveRainMapFactor;
        }

        return sunlight == maximumSunlight
            ? DirectSunlightFactor
            : IndirectSunlightFactor;
    }
}
