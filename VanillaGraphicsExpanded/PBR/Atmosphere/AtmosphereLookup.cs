using System;
using System.Collections.Immutable;
using System.Numerics;
using System.Threading;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>A coherent sky lookup and its hemispherical lighting integrals, expressed in scene-linear units.</summary>
internal sealed record AtmosphereLighting(Vector3 Sun, Vector3 Solar, Vector3 Environment, Vector3 Horizon, Vector3 Extinction, ImmutableArray<float> Sky)
{
    internal int Width { get; init; } = AtmosphereLookup.DefaultWidth;
    internal int Height { get; init; } = AtmosphereLookup.DefaultHeight;
    internal float HorizonElevation { get; init; }
    internal float Altitude { get; init; } = .001f;
    internal ImmutableArray<float> AerialRadiance { get; init; } = ImmutableArray<float>.Empty;
    internal ImmutableArray<float> AerialAttenuation { get; init; } = ImmutableArray<float>.Empty;
}

/// <summary>Integrates complete atmospheric snapshots, with optional bounded stepping for callers that need it.</summary>
internal sealed class AtmosphereLookup
{
    internal const int DefaultWidth = 32;
    internal const int DefaultHeight = 24;
    internal const int SamplesPerUpdate = 128;
    private float[] staging = Array.Empty<float>();
    private (int X, int Y, int Z, int Altitude, int Weather, int Width, int Height, int Quality, int Albedo)? completedKey, buildingKey;
    private int quality;
    private int width, height;
    private Vector3 sun, environment, horizon;
    private float altitude, aerosol;
    private int next;
    private AtmosphereMultipleScattering? multipleScattering;
    private float multipleScatteringAerosol;
    private float groundAlbedo, multipleScatteringAlbedo;
    internal AtmosphereLighting? Current { get; private set; }
    internal int Revision { get; private set; }

    #region Bounded refresh
    /// <summary>Builds a complete snapshot by default; explicit incremental callers can bound weather refresh work.</summary>
    internal bool Update(Vector3 solarDirection, float altitudeKm, float cloudCover, bool complete = true,
        int width = DefaultWidth, int height = DefaultHeight, CancellationToken cancellationToken = default, int quality = 0,
        float groundAlbedo = .1f)
    {
        if (!float.IsFinite(solarDirection.LengthSquared()) || solarDirection.LengthSquared() < .0001f
            || !float.IsFinite(altitudeKm) || !float.IsFinite(cloudCover)) return false;
        solarDirection = Vector3.Normalize(solarDirection);
        var key = ((int)MathF.Round(solarDirection.X * AtmosphereSolarDisk.DirectionResolution), (int)MathF.Round(solarDirection.Y * AtmosphereSolarDisk.DirectionResolution),
            (int)MathF.Round(solarDirection.Z * AtmosphereSolarDisk.DirectionResolution), (int)MathF.Round(Math.Clamp(altitudeKm, 0, 99) * 40),
            (int)MathF.Round(Math.Clamp(cloudCover, 0, 1) * 20), Width: Math.Clamp(width, 16, DefaultWidth * 4), Height: Math.Clamp(height, 8, DefaultHeight * 4), Quality: Math.Clamp(quality, 0, 3), Albedo: AtmosphereSeasonModel.AlbedoBucket(groundAlbedo));
        var previous = buildingKey ?? completedKey;
        bool resized = previous is { } prior && (prior.Width != key.Width || prior.Height != key.Height || prior.Quality != key.Quality);
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
            this.quality = key.Quality;
            int length = checked(this.width * this.height * 4);
            if (staging.Length != length) staging = new float[length];
            sun = solarDirection; altitude = Math.Clamp(altitudeKm, .001f, 99f);
            // Use the admitted weather bucket consistently for transport and its cached
            // medium table; tiny cloud jitter during sun motion must not rebuild the table.
            aerosol = 1f + 7f * (key.Item5 / 20f);
            this.groundAlbedo = key.Albedo / 50f;
            next = 0; environment = horizon = Vector3.Zero;
        }
        // Finish the admitted snapshot even if inputs change; otherwise moving weather could starve publication.
        var budget = AtmosphereScatteringBudget.FromQuality(this.quality);
        if (multipleScattering is null || multipleScatteringAerosol != aerosol || multipleScatteringAlbedo != this.groundAlbedo
            || multipleScattering.Width != budget.Width || multipleScattering.Height != budget.Height)
        {
            // Independent of observer and sun direction: retain one completed medium table.
            var replacement = AtmosphereMultipleScattering.Build(aerosol, cancellationToken, groundAlbedo: this.groundAlbedo,
                sunSamples: budget.Width, altitudeSamples: budget.Height,
                directionSamples: budget.DirectionSamples, raySamples: budget.RaySamples,
                lightSamples: budget.LightSamples);
            multipleScattering = replacement;
            multipleScatteringAerosol = aerosol;
            multipleScatteringAlbedo = this.groundAlbedo;
        }
        // Resolution belongs to the admitted build, not the latest requested settings.
        width = this.width; height = this.height;
        float horizonElevation = AtmosphereSkyMapping.Horizon(altitude);
        int end = Math.Min(next + (complete ? width * height : SamplesPerUpdate), width * height);
        Span<Vector3> directions = stackalloc Vector3[SamplesPerUpdate];
        Span<Vector3> radiances = stackalloc Vector3[SamplesPerUpdate];
        while (next < end)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = Math.Min(SamplesPerUpdate, end - next);
            for (int lane = 0; lane < count; lane++)
            {
                int index = next + lane, x = index % width, y = index / width;
                float elevation = AtmosphereSkyMapping.Elevation((float)y / (height - 1), horizonElevation);
                float azimuth = (x + .5f) / width * (2f * MathF.PI);
                directions[lane] = new(MathF.Cos(elevation) * MathF.Cos(azimuth), MathF.Sin(elevation), MathF.Cos(elevation) * MathF.Sin(azimuth));
            }
            AtmosphereModel.RadianceBatch(directions[..count], sun, altitude, aerosol, radiances[..count], multipleScattering);
            // Preserve row-major accumulation order for the shared lighting integrals.
            for (int lane = 0; lane < count; lane++, next++)
            {
                int y = next / width;
                Vector3 radiance = radiances[lane];
                staging[next * 4] = radiance.X; staging[next * 4 + 1] = radiance.Y;
                staging[next * 4 + 2] = radiance.Z; staging[next * 4 + 3] = 1;
                // Irradiance/pi is the Lambertian sky response at unit albedo, used by the shared environment model.
                environment += radiance * AtmosphereSkyMapping.EnvironmentWeight(y, width, height, horizonElevation);
            }
        }
        if (next != width * height) return false;
        // Shared horizontal lighting is evaluated at elevation zero, which differs from
        // the depressed planetary limb at altitude. Interpolate the same LUT as rendering.
        float horizontalRow = AtmosphereSkyMapping.Coordinate(0, horizonElevation) * (height - 1);
        int lower = Math.Min((int)horizontalRow, height - 2);
        horizon = Vector3.Zero;
        for (int x = 0; x < width; x++)
        {
            int a = (lower * width + x) * 4, b = a + width * 4;
            horizon += Vector3.Lerp(new(staging[a], staging[a + 1], staging[a + 2]),
                new(staging[b], staging[b + 1], staging[b + 2]), horizontalRow - lower) / width;
        }
        var aerial = AtmosphereAerialVolume.Build(sun, altitude, aerosol, width, height, staging, multipleScattering, cancellationToken);
        Current = new(sun, AtmosphereModel.SolarIrradiance(sun, altitude, aerosol), environment, horizon,
            AtmosphereModel.LocalExtinction(altitude, aerosol), ImmutableArray.CreateRange(staging))
            { Width = width, Height = height, HorizonElevation = horizonElevation, Altitude = altitude,
                AerialRadiance = aerial.Radiance, AerialAttenuation = aerial.Attenuation };
        completedKey = buildingKey; buildingKey = null; Revision++;
        return true;
    }
    #endregion
}
