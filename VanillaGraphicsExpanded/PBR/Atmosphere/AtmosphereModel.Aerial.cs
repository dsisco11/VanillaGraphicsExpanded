using System;
using System.Numerics;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Integrates finite camera paths for the aerial-perspective volume.</summary>
internal static partial class AtmosphereModel
{
    #region Finite path integration
    /// <summary>Builds cumulative radiance and transmission with two analytic-medium samples per distance interval.</summary>
    internal static void AerialRay(Vector3 direction, Vector3 sun, float altitude, float aerosol,
        AtmosphereMultipleScattering? multipleScattering, Span<Vector4> radiances, Span<Vector4> transmissions)
    {
        direction = Vector3.Normalize(direction); sun = Vector3.Normalize(sun);
        Vector3 origin = new(0, GroundRadius + Math.Clamp(altitude, .001f, 99f), 0);
        float boundary = Boundary(origin, direction, TopRadius), ground = GroundDistance(origin, direction);
        if (ground > 0) boundary = MathF.Min(boundary, ground);
        float cosine = Math.Clamp(Vector3.Dot(direction, sun), -1f, 1f);
        float rayleigh = 3f * (1f + cosine * cosine) / (16f * MathF.PI);
        const float g = .76f;
        float mie = (1f - g * g) / (4f * MathF.PI * MathF.Pow(1f + g * g - 2f * g * cosine, 1.5f));
        Vector3 throughput = Vector3.One, radiance = Vector3.Zero;
        radiances[0] = new(0, 0, 0, 1); transmissions[0] = Vector4.One;
        float previous = 0;
        for (int slice = 1; slice < AtmosphereAerialPerspective.Depth; slice++)
        {
            float end = AtmosphereAerialPerspective.Distance(slice, boundary);
            float step = (end - previous) * .5f;
            for (int sample = 0; sample < 2 && step > 0; sample++)
            {
                Vector3 point = origin + direction * (previous + (sample + .5f) * step);
                float radius = point.Length();
                Vector3 density = Density(MathF.Max(0, radius - GroundRadius));
                Vector3 extinction = Extinction(density, aerosol);
                Vector3 source = Transmittance(point, sun, aerosol)
                    * (Rayleigh * (density.X * rayleigh) + new Vector3(.003996f * aerosol * density.Y * mie));
                if (multipleScattering is not null)
                    source += (Rayleigh * density.X + new Vector3(.003996f * aerosol * density.Y))
                        * multipleScattering.Sample(radius - GroundRadius, Vector3.Dot(point, sun) / radius);
                // Integrating each locally constant medium analytically avoids negative
                // transmission and over-bright thick segments on the logarithmic grid.
                Vector3 integral = new(SegmentIntegral(extinction.X, step), SegmentIntegral(extinction.Y, step), SegmentIntegral(extinction.Z, step));
                radiance += throughput * source * integral;
                throughput *= ExpNegative(extinction * step);
            }
            radiances[slice] = new(Vector3.Max(Vector3.Zero, radiance * Solar), 1);
            transmissions[slice] = new(throughput, 1);
            previous = end;
        }
    }
    #endregion
}
