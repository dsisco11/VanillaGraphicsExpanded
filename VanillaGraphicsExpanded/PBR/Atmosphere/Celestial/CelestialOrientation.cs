using System;
using System.Numerics;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;

/// <summary>Derives a camera-independent celestial frame from calendar time and latitude.</summary>
internal static class CelestialOrientation
{
    #region Public API
    /// <summary>Captures calendar inputs directly, without relying on the suppressed native draw's matrices.</summary>
    internal static Matrix4x4 Capture(ICoreClientAPI api) => Create(api.World.Calendar.TotalDays,
        api.World.Calendar.YearRel, api.World.Calendar.OnGetLatitude?.Invoke(api.World.Player.Entity.Pos.Z) ?? .5);

    /// <summary>Matches the installed daily-minus-year phase and tilted rotation axis with a proper orthonormal frame.</summary>
    internal static Matrix4x4 Create(double totalDays, double yearRelative, double latitude)
    {
        if (!double.IsFinite(totalDays) || !double.IsFinite(yearRelative) || !double.IsFinite(latitude) || Math.Abs(latitude) > 1)
            throw new ArgumentException("Celestial orientation requires finite calendar inputs and normalized latitude.");
        double phase = totalDays - yearRelative;
        float angle = (float)((phase - Math.Floor(phase)) * (2 * Math.PI));
        // The installed axis is (0,-sin(tilt),cos(tilt)), tilt = |latitude*pi/2-obliquity|.
        // Map the declared +Y pole onto that axis, then turn the entire fictional sky about it.
        float tilt = MathF.Abs((float)latitude * MathF.PI / 2 - .40910518f);
        Vector3 pole = new(0, -MathF.Sin(tilt), MathF.Cos(tilt));
        Vector3 x = Vector3.Transform(Vector3.UnitX, Quaternion.CreateFromAxisAngle(pole, angle));
        Vector3 z = Vector3.Cross(x, pole);
        return new(x.X,x.Y,x.Z,0, pole.X,pole.Y,pole.Z,0, z.X,z.Y,z.Z,0, 0,0,0,1);
    }
    #endregion
}
