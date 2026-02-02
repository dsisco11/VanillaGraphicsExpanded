namespace VanillaGraphicsExpanded.LumOn.Scene;

internal static class TraceSceneRegionPriorityConstants
{
    public const float DistanceWeight = 1000f;

    public const float StaleBonus = 250f;

    public const float NeverAppliedBonus = 150f;

    public const float SeenLoadedRecentlyBonus = 75f;

    public const int SeenLoadedRecentTicks = 120;

    public const float MissingPenaltyPerStreak = 200f;
}
