using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Immutable climate-derived regional assumptions; no block or terrain-cover references escape capture.</summary>
internal readonly record struct AtmosphereSeasonSnapshot(double Latitude, double YearProgress,
    float MeanTemperature, float TemperatureTrend, float SnowCoverage, float GroundAlbedo);

/// <summary>Captures bounded calendar/climate samples on the game thread, with no terrain or snow-cover scanning.</summary>
internal sealed class AtmosphereSeasonInputs
{
    private (int X, int Z, int Dimension, long Time, int YearLength, float? Override)? key;
    private AtmosphereSeasonSnapshot current = new(0, 0, 0, 0, 0, AtmosphereSeasonModel.BareGroundAlbedo);
    private bool unavailable;
    private long retryAfter;

    #region Climate capture
    /// <summary>Estimates a region at sea level, refreshing per 64-block cell or one eighth of a game day.</summary>
    internal AtmosphereSeasonSnapshot Capture(ICoreClientAPI api, BlockPos playerPosition)
    {
        var calendar = api.World.Calendar;
        int yearLength = calendar.DaysPerYear;
        double now = calendar.TotalDays;
        if (!double.IsFinite(now) || yearLength <= 0) return current;
        long time = (long)Math.Floor(now * 8);
        var nextKey = (playerPosition.X >> 6, playerPosition.Z >> 6, playerPosition.dimension,
            time, yearLength, calendar.SeasonOverride);
        if (key == nextKey && (!unavailable || api.World.ElapsedMilliseconds < retryAfter)) return current;
        key = nextKey;
        unavailable = false;
        double sampleDay = time / 8d;
        double yearProgress = sampleDay / yearLength - Math.Floor(sampleDay / yearLength);
        // Climate maps describe the normal world only. Alternate dimensions and absent
        // regional climate use the neutral boundary rather than fabricated polar snow.
        current = new(0, yearProgress, 0, 0, 0, AtmosphereSeasonModel.BareGroundAlbedo);
        if (playerPosition.dimension != 0) return current;
        var position = new BlockPos((nextKey.Item1 << 6) + 32, api.World.SeaLevel, (nextKey.Item2 << 6) + 32, 0);
        double latitude = calendar.OnGetLatitude?.Invoke(position.Z) ?? .5;
        var accessor = api.World.BlockAccessor;
        var climate = accessor.GetClimateAt(position, EnumGetClimateMode.WorldGenValues);
        if (climate is null)
        {
            // Climate can become available as its region loads. Retry a missing region
            // at a bounded wall-clock cadence instead of waiting for the next date bucket.
            unavailable = true; retryAfter = api.World.ElapsedMilliseconds + 1000;
            current = current with { Latitude = latitude };
            return current;
        }
        double window = yearLength / 32d;
        float mean = MeanTemperature(accessor, position, climate, sampleDay, window);
        // A pinned season has no seasonal warming/cooling direction. Keep its current
        // mean but do not mistake the climate noise at other dates for a seasonal trend.
        float past = calendar.SeasonOverride.HasValue ? mean : MeanTemperature(accessor, position, climate, sampleDay - window, window);
        float future = calendar.SeasonOverride.HasValue ? mean : MeanTemperature(accessor, position, climate, sampleDay + window, window);
        var estimate = AtmosphereSeasonModel.Estimate(mean, past, future);
        current = new(latitude, yearProgress, mean, (future - past) * .5f, estimate.SnowCoverage, estimate.GroundAlbedo);
        return current;
    }

    /// <summary>Averages three dates and four times of day, suppressing diurnal and short weather variation.</summary>
    private static float MeanTemperature(IBlockAccessor accessor, BlockPos position, ClimateCondition climate,
        double day, double window)
    {
        float sum = 0;
        for (int date = -1; date <= 1; date++)
        {
            double sampleDate = Math.Floor(day + date * window * .5);
            for (int hour = 0; hour < 4; hour++)
            {
                // The overload reuses worldgen climate and delegates season/hemisphere/
                // season-override handling to the game's temperature implementation.
                var result = accessor.GetClimateAt(position, climate, EnumGetClimateMode.ForSuppliedDate_TemperatureOnly,
                    sampleDate + (hour + .5) / 4d);
                if (result is null || !float.IsFinite(result.Temperature)) return float.NaN;
                sum += result.Temperature;
            }
        }
        return sum / 12f;
    }
    #endregion
}
