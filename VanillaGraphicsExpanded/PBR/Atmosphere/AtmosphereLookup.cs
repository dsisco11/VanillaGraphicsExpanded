using System;
using System.Collections.Immutable;
using System.Numerics;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>A coherent sky lookup and its hemispherical lighting integrals, expressed in scene-linear units.</summary>
internal sealed record AtmosphereLighting(Vector3 Sun, Vector3 Solar, Vector3 Environment, Vector3 Horizon, Vector3 Extinction, ImmutableArray<float> Sky);

/// <summary>Incrementally computes a bounded lat-long sky table; consumers keep the last complete snapshot during refresh.</summary>
internal sealed class AtmosphereLookup
{
    internal const int Width = 32;
    internal const int Height = 24;
    internal const int SamplesPerUpdate = 32;
    private readonly float[] staging = new float[Width * Height * 4];
    private (int X, int Y, int Z, int Altitude, int Weather)? completedKey, buildingKey;
    private Vector3 sun, environment, horizon;
    private float altitude, aerosol;
    private int next;
    internal AtmosphereLighting? Current { get; private set; }
    internal int Revision { get; private set; }

    #region Bounded refresh
    /// <summary>Uses quantized atmospheric inputs; complete builds the initial table synchronously before consumers draw.</summary>
    internal bool Update(Vector3 solarDirection, float altitudeKm, float cloudCover, bool complete = false)
    {
        if (!float.IsFinite(solarDirection.LengthSquared()) || solarDirection.LengthSquared() < .0001f
            || !float.IsFinite(altitudeKm) || !float.IsFinite(cloudCover)) return false;
        solarDirection = Vector3.Normalize(solarDirection);
        var key = ((int)MathF.Round(solarDirection.X * 256), (int)MathF.Round(solarDirection.Y * 256),
            (int)MathF.Round(solarDirection.Z * 256), (int)MathF.Round(Math.Clamp(altitudeKm, 0, 99) * 40),
            (int)MathF.Round(Math.Clamp(cloudCover, 0, 1) * 20));
        if (buildingKey is null)
        {
            if (completedKey == key) return false;
            buildingKey = key;
            sun = solarDirection; altitude = Math.Clamp(altitudeKm, .001f, 99f);
            aerosol = 1f + 7f * Math.Clamp(cloudCover, 0, 1);
            next = 0; environment = horizon = Vector3.Zero;
        }
        // Finish the admitted snapshot even if inputs change; otherwise moving weather could starve publication.
        int end = Math.Min(next + (complete ? Width * Height : SamplesPerUpdate), Width * Height);
        for (; next < end; next++)
        {
            int x = next % Width, y = next / Width;
            float elevation = ((y + .5f) / Height - .5f) * MathF.PI;
            float azimuth = (x + .5f) / Width * (2f * MathF.PI);
            Vector3 direction = new(MathF.Cos(elevation) * MathF.Cos(azimuth), MathF.Sin(elevation), MathF.Cos(elevation) * MathF.Sin(azimuth));
            Vector3 radiance = AtmosphereModel.Radiance(direction, sun, altitude, aerosol);
            staging[next * 4] = radiance.X; staging[next * 4 + 1] = radiance.Y;
            staging[next * 4 + 2] = radiance.Z; staging[next * 4 + 3] = 1;
            // Irradiance/pi is the Lambertian sky response at unit albedo, used by the shared environment model.
            if (direction.Y > 0) environment += radiance * (direction.Y * MathF.Cos(elevation) * 2f * MathF.PI / (Width * Height));
            if (y == Height / 2) horizon += radiance / Width;
        }
        if (next != Width * Height) return false;
        Current = new(sun, AtmosphereModel.SolarIrradiance(sun, altitude, aerosol), environment, horizon,
            AtmosphereModel.LocalExtinction(altitude, aerosol), ImmutableArray.CreateRange(staging));
        completedKey = buildingKey; buildingKey = null; Revision++;
        return true;
    }
    #endregion
}
