using System;
using System.Numerics;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Integrates the isotropic source and feedback used by the atmospheric multiple-scattering approximation.</summary>
internal static partial class AtmosphereModel
{
    #region Isotropic transfer
    /// <summary>Returns unit-solar incident radiance and the response to a unit isotropic radiance field along one ray.</summary>
    internal static (Vector3 Source, Vector3 Feedback) MultipleScatteringTransfer(Vector3 direction,
        Vector3 sun, float altitude, float aerosol, float groundAlbedo, int samples,
        int lightSamples = AtmosphereMultipleScattering.DefaultLightSamples)
    {
        Vector3 origin = new(0, GroundRadius + altitude, 0);
        float ground = GroundDistance(origin, direction);
        float distance = ground > 0 ? ground : Boundary(origin, direction, TopRadius);
        Vector3 throughput = Vector3.One, source = Vector3.Zero, feedback = Vector3.Zero;
        for (int i = 0; i < samples; i++)
        {
            float start = distance * i * i / (samples * samples);
            float end = distance * (i + 1) * (i + 1) / (samples * samples);
            Vector3 point = origin + direction * ((start + end) * .5f);
            Vector3 density = Density(MathF.Max(0, point.Length() - GroundRadius));
            Vector3 extinction = Extinction(density, aerosol);
            Vector3 scattering = Rayleigh * density.X + new Vector3(.003996f * aerosol * density.Y);
            Vector3 segment = ExpNegative(extinction * (end - start));
            // Analytic constant-medium segment integration bounds feedback by the fraction
            // of light removed. Midpoint source*distance can exceed unity in thick haze.
            Vector3 integrated = new(SegmentIntegral(extinction.X, end - start),
                SegmentIntegral(extinction.Y, end - start), SegmentIntegral(extinction.Z, end - start));
            Vector3 response = throughput * scattering * integrated;
            feedback += response;
            source += response * Transmittance(point, sun, aerosol, lightSamples) / (4f * MathF.PI);
            throughput *= segment;
        }
        if (ground > 0)
        {
            Vector3 normal = Vector3.Normalize(origin + direction * ground);
            // A uniform Lambertian planet is an atmospheric boundary approximation, not
            // sampled game terrain. Include its direct illumination and isotropic feedback.
            Vector3 reflected = throughput * groundAlbedo;
            source += reflected * Transmittance(normal * (GroundRadius + .001f), sun, aerosol, lightSamples)
                * (MathF.Max(0, Vector3.Dot(normal, sun)) / MathF.PI);
            feedback += reflected;
        }
        return (source, feedback);
    }

    /// <summary>Integrates exponential attenuation with a stable thin-segment limit.</summary>
    private static float SegmentIntegral(float extinction, float distance)
    {
        float optical = extinction * distance;
        return optical < .001f ? distance * (1f - optical * .5f + optical * optical / 6f)
            : (1f - MathF.Exp(-optical)) / extinction;
    }
    #endregion
}
