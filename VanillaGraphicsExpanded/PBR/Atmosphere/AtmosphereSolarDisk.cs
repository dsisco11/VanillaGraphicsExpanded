using System;
using System.Numerics;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Defines the finite solar emitter shared by disk rendering and direct illumination.</summary>
internal static class AtmosphereSolarDisk
{
    // Half the vanilla bright disk's angular diameter, excluding its translucent halo:
    // 0.5 * atan((48 texture pixels * 0.04 scale) / (50 distance - 128 * 0.04 offset)).
    internal const float AngularRadius = .021377339f;
    // Resolve the sunrise transition instead of quantizing it into two or three atmosphere builds.
    internal const int DirectionResolution = 1 << 16;

    #region Disk geometry
    /// <summary>Returns the visible circular segment area divided by the full disk area.</summary>
    internal static float Visibility(float elevation, float horizon)
    {
        float q = Math.Clamp((elevation - horizon) / AngularRadius, -1f, 1f);
        if (q < -.9f) return SmallSegment(1f + q);
        if (q > .9f) return 1f - SmallSegment(1f - q);
        return (MathF.Acos(-q) + q * MathF.Sqrt(MathF.Max(0, 1f - q * q))) / MathF.PI;
    }

    /// <summary>Evaluates a thin circular segment without subtracting nearly equal acos and chord terms.</summary>
    private static float SmallSegment(float height) => (4f * MathF.Sqrt(2f) / (3f * MathF.PI))
        * height * MathF.Sqrt(height) * (1f - height * (3f / 20f + height * (3f / 224f + height / 384f)));

    /// <summary>Locates the centroid of the unoccluded segment for bounded atmospheric transmission evaluation.</summary>
    internal static float VisibleElevation(float elevation, float horizon, float visibility)
    {
        float q = Math.Clamp((elevation - horizon) / AngularRadius, -1f, 1f);
        // Factor the chord to retain thin-segment precision on both fused and unfused hardware.
        float chord = MathF.Max(0, (1f - q) * (1f + q));
        return elevation + AngularRadius * (2f / (3f * MathF.PI)) * chord * MathF.Sqrt(chord) / visibility;
    }

    /// <summary>Converts disk-integrated normal irradiance to uniform visible disk radiance.</summary>
    internal static Vector3 Radiance(AtmosphereLighting lighting)
    {
        float visible = Visibility(MathF.Asin(Math.Clamp(lighting.Sun.Y, -1, 1)), lighting.HorizonElevation);
        float sine = MathF.Sin(AngularRadius);
        return visible > 0 ? lighting.Solar / (visible * MathF.PI * sine * sine) : Vector3.Zero;
    }
    #endregion
}
