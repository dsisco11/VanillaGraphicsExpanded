using System;
using System.Globalization;

namespace VanillaGraphicsExpanded.PBR.Materials.Cache;

/// <summary>
/// Stable, deterministic inputs used when building average-base-color cache keys.
/// </summary>
internal readonly record struct BaseColorCacheKeyInputs(int SchemaVersion, string StablePrefix)
{
    public static BaseColorCacheKeyInputs CreateDefaults()
    {
        // Bump whenever the stable prefix contract changes.
        const int SchemaVersion = 1;

        // Version 2: 5% symmetric luminance trimming, exact sRGB LUT, bounded stratified sampling.
        const int AvgAlgoVersion = 2;

        string prefix = string.Format(
            CultureInfo.InvariantCulture,
            "schema={0}|avgAlgo={1}",
            SchemaVersion,
            AvgAlgoVersion);

        return new BaseColorCacheKeyInputs(SchemaVersion, prefix);
    }
}
