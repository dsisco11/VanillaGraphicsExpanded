using System;
using System.Collections.Immutable;
using System.Numerics;
using System.Threading;

namespace VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;

/// <summary>Immutable complete catalog, diffuse quadrature and calibration shared by visible and incident lighting.</summary>
/// <param name="ResolvedStarMaximumFluxLux">Precomputed sum of displayed stars' maximum RGB flux, bounding arbitrary overlap.</param>
internal sealed record CelestialSource(CelestialQuality Quality, ImmutableArray<CelestialStar> Stars,
    ImmutableArray<CelestialLightSample> Quadrature, float GalaxyAmplitude, float StorageScale, float MaximumNits,
    double ResolvedStarMaximumFluxLux)
{
    internal const int StarCapacity = 4096;
    internal const int QuadratureSize = 64;
    internal const float GalaxyLux = .0003f, StarsLux = .0001f, AirglowLux = .0004f;
    /// <summary>Identifies deterministic inputs and physical calibration without depending on exposure or viewport.</summary>
    internal string Identity => $"{CelestialProceduralField.Version}:{CelestialProceduralField.Seed:X8}:{Quality.Level}:{GalaxyAmplitude:R}";

    #region Public API
    /// <summary>Builds a bounded fictional source using the existing clear-reference extinction; safe on a worker thread.</summary>
    internal static CelestialSource Create(CelestialQuality quality, CancellationToken cancellation = default)
    {
        var stars = new CelestialStar[StarCapacity];
        for (uint id = 0; id < StarCapacity; id++)
        {
            cancellation.ThrowIfCancellationRequested();
            float z = 1 - 2 * CelestialProceduralField.Random(id, 1), angle = 2 * MathF.PI * CelestialProceduralField.Random(id, 2);
            float radius = MathF.Sqrt(MathF.Max(0, 1 - z * z));
            // +Y is the celestial pole, unlike conventional mathematical sphere coordinates.
            Vector3 direction = new(radius * MathF.Cos(angle), z, radius * MathF.Sin(angle));
            float magnitude = -1 + 8 * MathF.Pow(CelestialProceduralField.Random(id, 3), 1f / 3);
            float flux = (float)(108000 / Math.Pow(206264.806247, 2)) * MathF.Pow(10, -.4f * magnitude);
            stars[id] = new((int)id, direction, magnitude, magnitude, CelestialProceduralField.Color(CelestialProceduralField.Random(id, 4)), flux);
        }
        Array.Sort(stars, (a, b) => { int comparison = a.RawMagnitude.CompareTo(b.RawMagnitude); return comparison == 0 ? a.Id.CompareTo(b.Id) : comparison; });
        // Uniformly rotating any direction samples this same ground-level cosine-weighted transmission.
        Vector3 response = ReferenceResponse();
        double starResponse = 0;
        foreach (var star in stars) starResponse += star.FluxLux * Vector3.Dot(star.Color * response, CelestialProceduralField.Luma);
        float starScale = PositiveScale(StarsLux, starResponse);
        float omittedMaximum = 0;
        double resolvedMaximum = 0;
        for (int i = 0; i < stars.Length; i++)
        {
            var star = stars[i]; stars[i] = star with { FluxLux = star.FluxLux * starScale,
                CalibratedMagnitude = star.RawMagnitude - 2.5f * MathF.Log10(starScale) };
            // Color and calibrated flux are fixed for this immutable source. Compute
            // each peak once and retain the complete displayed subset's overlap bound.
            float peakFlux = stars[i].FluxLux * Math.Max(star.Color.X, Math.Max(star.Color.Y, star.Color.Z));
            if (i >= quality.StarCount) omittedMaximum += peakFlux;
            else resolvedMaximum += peakFlux;
        }
        var quadrature = ImmutableArray.CreateBuilder<CelestialLightSample>(6 * QuadratureSize * QuadratureSize);
        Vector3 integral = Vector3.Zero;
        for (int face = 0; face < 6; face++) for (int y = 0; y < QuadratureSize; y++) for (int x = 0; x < QuadratureSize; x++)
        {
            if (x == 0) cancellation.ThrowIfCancellationRequested();
            Vector3 direction = CelestialCubeGeometry.Direction(face, 2f * (x + .5f) / QuadratureSize - 1, 2f * (y + .5f) / QuadratureSize - 1);
            float area = CelestialCubeGeometry.SolidAngle(x, y, QuadratureSize);
            Vector3 radiance = CelestialProceduralField.Evaluate(direction, 7, 2f / QuadratureSize);
            quadrature.Add(new(direction, radiance, area)); integral += radiance * area;
        }
        float galaxyScale = PositiveScale(GalaxyLux, Vector3.Dot(integral * response, CelestialProceduralField.Luma));
        for (int i = 0; i < quadrature.Count; i++) quadrature[i] = quadrature[i] with { Radiance = quadrature[i].Radiance * galaxyScale };
        // The normalized residual kernel peaks at 1/(pi*h); allow discrete normalization headroom.
        // Include packed rounding and FP32 reconstruction headroom in the published physical bound.
        float maximum = (4.55f * 1.456f * galaxyScale + omittedMaximum * CelestialResidual.KernelMaximum / .95f) * 1.02f;
        float scale = MathF.Pow(2, MathF.Floor(MathF.Log2(32000 / maximum)));
        if (!float.IsFinite(maximum) || maximum <= 0 || !float.IsFinite(scale) || scale <= 0 || maximum * scale > 32000)
            throw new ArgumentException("Celestial source lies outside the admitted HDR domain.");
        return new(quality, ImmutableArray.Create(stars), quadrature.MoveToImmutable(), galaxyScale, scale, maximum, resolvedMaximum);
    }
    #endregion

    #region Private
    /// <summary>Integrates uniform rotations through clear atmospheric transport, normalized by the sphere's area.</summary>
    private static Vector3 ReferenceResponse()
    {
        Vector3 response = Vector3.Zero;
        const int samples = 1024;
        for (int i = 0; i < samples; i++)
        {
            float y = (i + .5f) / samples;
            response += AtmosphereModel.ExternalTransmittance(new(MathF.Sqrt(1 - y * y), y, 0), .001f, 1) * (y / (2 * samples));
        }
        return response;
    }

    /// <summary>Rejects missing physical reference power instead of manufacturing a brightness floor.</summary>
    private static float PositiveScale(float target, double response)
    {
        double scale = target / response;
        if (!double.IsFinite(scale) || scale <= 0 || scale > float.MaxValue) throw new ArgumentException("Invalid celestial calibration response.");
        return (float)scale;
    }
    #endregion
}

/// <summary>Stores a stable star identity, celestial direction, linear color and integrated photometric power.</summary>
internal readonly record struct CelestialStar(int Id, Vector3 Direction, float RawMagnitude, float CalibratedMagnitude, Vector3 Color, float FluxLux);

/// <summary>Stores diffuse source radiance and exact integration area before orientation and atmospheric attenuation.</summary>
internal readonly record struct CelestialLightSample(Vector3 Direction, Vector3 Radiance, float SolidAngle);
