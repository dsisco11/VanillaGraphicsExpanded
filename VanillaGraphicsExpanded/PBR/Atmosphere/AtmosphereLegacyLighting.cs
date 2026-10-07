using System;
using System.Numerics;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Projects physical lighting into the engine's bounded legacy color and visibility conventions.</summary>
internal readonly record struct AtmosphereLegacyLighting(Vector3 Ambient, Vector3 Fog, float Daylight)
{
    #region Public API
    /// <summary>Uses a white sun-facing Lambertian reference for the legacy combined sunlight multiplier.</summary>
    internal static AtmosphereLegacyLighting From(AtmosphereLighting lighting)
    {
        // Legacy voxel sunlight already supplies visibility. Supply combined color/intensity,
        // without applying voxel visibility, weather brightness or engine ambient modifiers twice.
        Vector3 response = Vector3.Max(Vector3.Zero, lighting.Environment + lighting.Solar / MathF.PI);
        Vector3 ambient = ResolveDisplay(response);
        float daylight = Math.Clamp(Vector3.Dot(ambient, new(.2126f, .7152f, .0722f)), 0, 1);
        return new(ambient, ResolveDisplay(lighting.Horizon), daylight);
    }
    #endregion

    #region Private
    /// <summary>Matches the unit-exposure, peak-preserving shoulder and sRGB transfer in pbr_color.glsl.</summary>
    private static Vector3 ResolveDisplay(Vector3 radiance)
    {
        radiance = Vector3.Max(Vector3.Zero, radiance);
        Vector3 mapped = radiance / (1 + MathF.Max(radiance.X, MathF.Max(radiance.Y, radiance.Z)));
        return new(Encode(mapped.X), Encode(mapped.Y), Encode(mapped.Z));
    }
    /// <summary>Encodes one nonnegative linear channel for legacy display-space consumers.</summary>
    private static float Encode(float value) => value <= .0031308f ? value * 12.92f : 1.055f * MathF.Pow(value, 1 / 2.4f) - .055f;
    #endregion
}
