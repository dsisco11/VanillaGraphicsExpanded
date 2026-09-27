using System;
using System.Collections.Immutable;
using System.Numerics;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>A coherent sky lookup and its hemispherical lighting integrals, expressed in scene-linear units.</summary>
internal sealed record AtmosphereLighting(Vector3 Sun, Vector3 Solar, Vector3 Environment, Vector3 Horizon, Vector3 Extinction, ImmutableArray<float> Sky)
{
    internal int Width { get; init; } = AtmosphereLookup.DefaultWidth;
    internal int Height { get; init; } = AtmosphereLookup.DefaultHeight;
}

/// <summary>Builds resolution changes immediately and refreshes atmospheric inputs incrementally, publishing complete snapshots.</summary>
internal sealed class AtmosphereLookup
{
    internal const int DefaultWidth = 32;
    internal const int DefaultHeight = 24;
    internal const int SamplesPerUpdate = 128;
    private float[] staging = Array.Empty<float>();
    private (int X, int Y, int Z, int Altitude, int Weather, int Width, int Height)? completedKey, buildingKey;
    private int width, height;
    private Vector3 sun, environment, horizon;
    private float altitude, aerosol;
    private int next;
    internal AtmosphereLighting? Current { get; private set; }
    internal int Revision { get; private set; }

    #region Bounded refresh
    /// <summary>Refreshes weather incrementally; initialization and resolution changes complete synchronously.</summary>
    internal bool Update(Vector3 solarDirection, float altitudeKm, float cloudCover, bool complete = false,
        int width = DefaultWidth, int height = DefaultHeight)
    {
        if (!float.IsFinite(solarDirection.LengthSquared()) || solarDirection.LengthSquared() < .0001f
            || !float.IsFinite(altitudeKm) || !float.IsFinite(cloudCover)) return false;
        solarDirection = Vector3.Normalize(solarDirection);
        var key = ((int)MathF.Round(solarDirection.X * 256), (int)MathF.Round(solarDirection.Y * 256),
            (int)MathF.Round(solarDirection.Z * 256), (int)MathF.Round(Math.Clamp(altitudeKm, 0, 99) * 40),
            (int)MathF.Round(Math.Clamp(cloudCover, 0, 1) * 20), Width: Math.Clamp(width, 16, DefaultWidth << 3), Height: Math.Clamp(height, 8, DefaultHeight << 3));
        var previous = buildingKey ?? completedKey;
        bool resized = previous is { } prior && (prior.Width != key.Width || prior.Height != key.Height);
        if (resized)
        {
            // A resolution edit supersedes pending weather work and uses the latest inputs.
            // Finish it in this call so rendering can publish the replacement immediately.
            buildingKey = null;
            complete = true;
        }
        if (buildingKey is null)
        {
            if (completedKey == key && !resized) return false;
            buildingKey = key;
            this.width = key.Width; this.height = key.Height;
            int length = checked(this.width * this.height * 4);
            if (staging.Length != length) staging = new float[length];
            sun = solarDirection; altitude = Math.Clamp(altitudeKm, .001f, 99f);
            aerosol = 1f + 7f * Math.Clamp(cloudCover, 0, 1);
            next = 0; environment = horizon = Vector3.Zero;
        }
        // Finish the admitted snapshot even if inputs change; otherwise moving weather could starve publication.
        // Resolution belongs to the admitted build, not the latest requested settings.
        width = this.width; height = this.height;
        int end = Math.Min(next + (complete ? width * height : SamplesPerUpdate), width * height);
        for (; next < end; next++)
        {
            int x = next % width, y = next / width;
            float elevation = ((y + .5f) / height - .5f) * MathF.PI;
            float azimuth = (x + .5f) / width * (2f * MathF.PI);
            Vector3 direction = new(MathF.Cos(elevation) * MathF.Cos(azimuth), MathF.Sin(elevation), MathF.Cos(elevation) * MathF.Sin(azimuth));
            Vector3 radiance = AtmosphereModel.Radiance(direction, sun, altitude, aerosol);
            staging[next * 4] = radiance.X; staging[next * 4 + 1] = radiance.Y;
            staging[next * 4 + 2] = radiance.Z; staging[next * 4 + 3] = 1;
            // Irradiance/pi is the Lambertian sky response at unit albedo, used by the shared environment model.
            if (direction.Y > 0) environment += radiance * (direction.Y * MathF.Cos(elevation) * 2f * MathF.PI / (width * height));
            if (y == height / 2) horizon += radiance / width;
        }
        if (next != width * height) return false;
        Current = new(sun, AtmosphereModel.SolarIrradiance(sun, altitude, aerosol), environment, horizon,
            AtmosphereModel.LocalExtinction(altitude, aerosol), ImmutableArray.CreateRange(staging)) { Width = width, Height = height };
        completedKey = buildingKey; buildingKey = null; Revision++;
        return true;
    }
    #endregion
}
