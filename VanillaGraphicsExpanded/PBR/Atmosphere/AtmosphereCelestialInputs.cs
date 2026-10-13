using System;
using System.Numerics;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Captures independent celestial directions and the native eight-step phase coordinate.</summary>
internal readonly record struct AtmosphereCelestialInputs(Vector3 Sun, Vector3 Moon, float Phase)
{
    #region Public API
    /// <summary>Normalizes calendar positions without adopting its artistic brightness or apparent size.</summary>
    internal static AtmosphereCelestialInputs Capture(IClientGameCalendar calendar)
    {
        var sun = calendar.SunPositionNormalized;
        var moon = calendar.MoonPosition;
        return new AtmosphereCelestialInputs(new(sun.X, sun.Y, sun.Z), new(moon.X, moon.Y, moon.Z),
            (float)calendar.MoonPhaseExact).Normalize();
    }

    /// <summary>Admits finite directions and wraps the phase at the native full-cycle boundary.</summary>
    internal AtmosphereCelestialInputs Normalize()
    {
        if (!float.IsFinite(Sun.LengthSquared()) || Sun.LengthSquared() < .0001f
            || !float.IsFinite(Moon.LengthSquared()) || Moon.LengthSquared() < .0001f
            || !float.IsFinite(Phase)) throw new ArgumentException("Celestial directions and phase must be finite.");
        return new(Vector3.Normalize(Sun), Vector3.Normalize(Moon), (Phase % 8 + 8) % 8);
    }

    /// <summary>Provides stable angular admission without making calendar motion rebuild every frame.</summary>
    internal (Vector3 Sun, Vector3 Moon, int Phase) Key =>
        (DirectionKey(Sun), DirectionKey(Moon), (int)MathF.Round(Phase * 1024));

    /// <summary>Matches the installed lunar sprite's opaque disk footprint rather than its transparent halo quad.</summary>
    internal float MoonAngularRadius => MathF.Atan(64 * .023100002f
        * (1.1f + Math.Clamp((Vector3.Dot(Sun, Moon) - .99f) * 40, 0, .2f)) / 50);
    #endregion

    #region Private
    /// <summary>Matches the existing transport direction buckets for each independent emitter.</summary>
    private static Vector3 DirectionKey(Vector3 value) => new(
        MathF.Round(value.X * AtmosphereSolarDisk.DirectionResolution),
        MathF.Round(value.Y * AtmosphereSolarDisk.DirectionResolution),
        MathF.Round(value.Z * AtmosphereSolarDisk.DirectionResolution));
    #endregion
}
