namespace VanillaGraphicsExpanded.LumOn.Scene;

internal static class TraceSceneRegionPriorityConstants
{
    public const int NearRadiusRegions = 4;

    public const float DistanceWeight = 1000f;

    public const float StaleBonus = 250f;

    public const float NeverAppliedBonus = 150f;

    public const float SeenLoadedRecentlyBonus = 75f;

    public const int SeenLoadedRecentTicks = 2000;

    public const float MissingPenaltyPerStreak = 200f;
}
