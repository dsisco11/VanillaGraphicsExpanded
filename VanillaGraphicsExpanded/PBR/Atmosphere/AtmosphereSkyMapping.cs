using System;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Maps sky rows around the observer's depressed planetary horizon and integrates their angular area.</summary>
internal static class AtmosphereSkyMapping
{
    #region Coordinates
    /// <summary>Returns the geometric horizon elevation for the admitted atmospheric observer.</summary>
    internal static float Horizon(float altitude) => -MathF.Acos(AtmosphereModel.GroundRadius /
        (AtmosphereModel.GroundRadius + Math.Clamp(altitude, .001f, 99f)));

    /// <summary>Quadratic spacing concentrates rows on both sides of the geometric horizon, including exact poles.</summary>
    internal static float Elevation(float v, float horizon)
    {
        float t = 2f * Math.Clamp(v, 0, 1) - 1f;
        return horizon + (t < 0 ? -(MathF.PI * .5f + horizon) : MathF.PI * .5f - horizon) * t * t;
    }

    /// <summary>Inverts the elevation mapping; callers convert row coordinates into texture-centre coordinates.</summary>
    internal static float Coordinate(float elevation, float horizon) => elevation < horizon
        ? .5f - .5f * MathF.Sqrt(Math.Clamp((horizon - elevation) / (MathF.PI * .5f + horizon), 0, 1))
        : .5f + .5f * MathF.Sqrt(Math.Clamp((elevation - horizon) / (MathF.PI * .5f - horizon), 0, 1));
    #endregion

    #region Shared illumination
    /// <summary>Integrates cosine-weighted solid angle over a row's upward-hemisphere cell, divided by pi.</summary>
    internal static float EnvironmentWeight(int row, int width, int height, float horizon)
    {
        float low = MathF.Max(0, Elevation((row - .5f) / (height - 1), horizon));
        float high = MathF.Max(0, Elevation((row + .5f) / (height - 1), horizon));
        float a = MathF.Sin(low), b = MathF.Sin(high);
        return (b * b - a * a) / width;
    }
    #endregion
}
