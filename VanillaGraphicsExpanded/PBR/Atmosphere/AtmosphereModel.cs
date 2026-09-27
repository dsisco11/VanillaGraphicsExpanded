using System;
using System.Numerics;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Bounded RGB single scattering in a spherical atmosphere; distances are kilometres and coefficients inverse kilometres.</summary>
internal static partial class AtmosphereModel
{
    internal const float GroundRadius = 6360f;
    internal const float TopRadius = 6460f;
    private const int ViewSamples = 24;
    private const int LightSamples = 12;
    private static readonly Vector3 Rayleigh = new(.005802f, .013558f, .0331f);
    private static readonly Vector3 Ozone = new(.000650f, .001881f, .000085f);
    // Relative solar irradiance: one scene-linear unit is the chosen reference extraterrestrial irradiance.
    private static readonly Vector3 Solar = new(1.474f, 1.8504f, 1.91198f);

    #region Integration
    /// <summary>Integrates sky radiance excluding the separately rendered solar disk and ground reflection.</summary>
    internal static Vector3 Radiance(Vector3 direction, Vector3 sun, float altitudeKm, float aerosol)
    {
        direction = Vector3.Normalize(direction);
        sun = Vector3.Normalize(sun);
        aerosol = Math.Clamp(aerosol, .1f, 8f);
        var origin = new Vector3(0, GroundRadius + Math.Clamp(altitudeKm, .001f, 99f), 0);
        float distance = Boundary(origin, direction, TopRadius);
        float ground = GroundDistance(origin, direction);
        if (ground > 0) distance = MathF.Min(distance, ground);
        Vector3 optical = Vector3.Zero, result = Vector3.Zero;
        float cosine = Math.Clamp(Vector3.Dot(direction, sun), -1f, 1f);
        float rayleighPhase = 3f * (1f + cosine * cosine) / (16f * MathF.PI);
        const float g = .76f;
        float miePhase = (1f - g * g) / (4f * MathF.PI * MathF.Pow(1f + g * g - 2f * g * cosine, 1.5f));
        for (int i = 0; i < ViewSamples; i++)
        {
            // Quadratic spacing resolves the dense boundary layer without increasing the integration budget.
            float start = distance * i * i / (ViewSamples * ViewSamples);
            float end = distance * (i + 1) * (i + 1) / (ViewSamples * ViewSamples);
            float step = end - start;
            Vector3 point = origin + direction * ((start + end) * .5f);
            float h = MathF.Max(0, point.Length() - GroundRadius);
            Vector3 density = Density(h);
            Vector3 extinction = Extinction(density, aerosol);
            Vector3 transmission = ExpNegative(optical + extinction * (.5f * step)) * Transmittance(point, sun, aerosol);
            Vector3 scattering = Rayleigh * (density.X * rayleighPhase)
                + new Vector3(.003996f * aerosol * density.Y * miePhase);
            result += transmission * scattering * step;
            optical += extinction * step;
        }
        return Vector3.Max(Vector3.Zero, result * Solar);
    }

    /// <summary>Returns direct solar irradiance, including extinction and the planet's horizon occlusion.</summary>
    internal static Vector3 SolarIrradiance(Vector3 sun, float altitudeKm, float aerosol) =>
        Solar * Transmittance(new(0, GroundRadius + Math.Clamp(altitudeKm, .001f, 99f), 0), Vector3.Normalize(sun), aerosol);

    /// <summary>Integrates optical depth to space; a ray intercepted by the solid planet has zero transmission.</summary>
    private static Vector3 Transmittance(Vector3 origin, Vector3 direction, float aerosol, int samples = LightSamples)
    {
        if (GroundDistance(origin, direction) > 0) return Vector3.Zero;
        float distance = Boundary(origin, direction, TopRadius);
        Vector3 optical = Vector3.Zero;
        for (int i = 0; i < samples; i++)
        {
            float start = distance * i * i / (samples * samples);
            float end = distance * (i + 1) * (i + 1) / (samples * samples);
            optical += Extinction(Density(MathF.Max(0, (origin + direction * ((start + end) * .5f)).Length() - GroundRadius)), aerosol) * (end - start);
        }
        return ExpNegative(optical);
    }
    #endregion

    #region Medium and geometry
    /// <summary>Local extinction per metre for the bounded homogeneous aerial-perspective approximation.</summary>
    internal static Vector3 LocalExtinction(float altitudeKm, float aerosol) => Extinction(Density(MathF.Max(0, altitudeKm)), aerosol) * .001f;

    /// <summary>Exponential molecular/aerosol densities and a triangular ozone layer centred at 25 km.</summary>
    private static Vector3 Density(float altitude) => new(MathF.Exp(-altitude / 8f), MathF.Exp(-altitude / 1.2f), MathF.Max(0, 1f - MathF.Abs(altitude - 25f) / 15f));

    /// <summary>Includes Rayleigh scattering, aerosol scattering/absorption and ozone absorption.</summary>
    private static Vector3 Extinction(Vector3 density, float aerosol) =>
        Rayleigh * density.X + new Vector3(.004440f * Math.Clamp(aerosol, .1f, 8f) * density.Y) + Ozone * density.Z;

    /// <summary>Evaluates Beer-Lambert transmission independently for each RGB wavelength band.</summary>
    private static Vector3 ExpNegative(Vector3 value) => new(MathF.Exp(-value.X), MathF.Exp(-value.Y), MathF.Exp(-value.Z));

    /// <summary>Returns the forward atmosphere-shell exit distance.</summary>
    private static float Boundary(Vector3 origin, Vector3 direction, float radius)
    {
        float b = Vector3.Dot(origin, direction);
        return MathF.Max(0, -b + MathF.Sqrt(MathF.Max(0, b * b - (origin.LengthSquared() - radius * radius))));
    }

    /// <summary>Returns the nearest positive planet intersection, or -1 when the ray remains in air.</summary>
    private static float GroundDistance(Vector3 origin, Vector3 direction)
    {
        float b = Vector3.Dot(origin, direction);
        float discriminant = b * b - (origin.LengthSquared() - GroundRadius * GroundRadius);
        return b < 0 && discriminant >= 0 ? -b - MathF.Sqrt(discriminant) : -1f;
    }
    #endregion
}
