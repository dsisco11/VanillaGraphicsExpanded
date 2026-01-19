namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;

internal static class LumOnWorldProbeMetaFlags
{
    public const uint Valid = 1u << 0;
    public const uint SkyOnly = 1u << 1;
    public const uint Stale = 1u << 2;
    public const uint InFlight = 1u << 3;
}
