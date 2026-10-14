using System;
using System.Collections.Immutable;
using System.Numerics;
using System.Threading;

namespace VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;

/// <summary>Integrates the complete immutable external source through its completed atmospheric transmission.</summary>
internal static class CelestialDiffuseLighting
{
    #region Public API
    /// <summary>Computes horizontal E/pi on a worker, independent of the displayed star prefix and map precision.</summary>
    internal static Vector3 Integrate(CelestialSource source, Matrix4x4 rotation,
        ImmutableArray<float> transmission, float horizon, float clouds, CancellationToken cancellation = default)
    {
        if (!Matrix4x4.Decompose(rotation, out var scale, out _, out var translation)
            || Vector3.Distance(scale, Vector3.One) > 1e-5f || translation != Vector3.Zero
            || !float.IsFinite(rotation.GetDeterminant()) || MathF.Abs(rotation.GetDeterminant()-1) > 1e-5f
            || rotation.M14 != 0 || rotation.M24 != 0 || rotation.M34 != 0 || rotation.M44 != 1
            || !float.IsFinite(horizon) || horizon < -MathF.PI/2 || horizon > 0
            || !float.IsFinite(clouds) || clouds < 0 || clouds > 1)
            throw new ArgumentException("Invalid celestial illumination inputs.");
        if (transmission.Length != AtmosphereExternalTransmission.SampleCount*4)
            throw new ArgumentException("Incomplete celestial atmospheric transmission.");
        foreach (float value in transmission)
            if (!float.IsFinite(value) || value < 0 || value > 1)
                throw new ArgumentException("Invalid celestial atmospheric transmission.");
        // Double accumulation preserves faint RGB when summing many differently sized angular cells.
        double r = 0, g = 0, b = 0;
        foreach (var sample in source.Quadrature)
        {
            cancellation.ThrowIfCancellationRequested();
            Vector3 direction = Vector3.TransformNormal(sample.Direction, rotation);
            if (direction.Y <= 0) continue;
            Vector3 t = AtmosphereExternalTransmission.Sample(transmission, MathF.Asin(Math.Clamp(direction.Y,-1,1)), horizon);
            Vector3 value = sample.Radiance * t;
            double weight = sample.SolidAngle * (double)direction.Y;
            r += value.X * weight; g += value.Y * weight; b += value.Z * weight;
        }
        // All catalog entries contribute exactly once, including those represented by map residuals for display.
        foreach (var star in source.Stars)
        {
            cancellation.ThrowIfCancellationRequested();
            Vector3 direction = Vector3.TransformNormal(star.Direction, rotation);
            if (direction.Y <= 0) continue;
            Vector3 t = AtmosphereExternalTransmission.Sample(transmission, MathF.Asin(Math.Clamp(direction.Y,-1,1)), horizon);
            Vector3 value = star.Color * t;
            double weight = star.FluxLux * (double)direction.Y;
            r += value.X * weight; g += value.Y * weight; b += value.Z * weight;
        }
        float cloudFactor = 1 - .9f * Math.Clamp(clouds,0,1);
        Vector3 result = new((float)(r/Math.PI), (float)(g/Math.PI), (float)(b/Math.PI));
        result *= cloudFactor;
        if (!LightingCalibration.FiniteNonnegative(result)) throw new ArgumentException("Invalid celestial diffuse power.");
        return result;
    }

    /// <summary>Adds source-derived diffuse power and the documented isotropic finite-path approximation without changing extinction.</summary>
    internal static AtmosphereLighting Apply(AtmosphereLighting lighting, CelestialSource source,
        Matrix4x4 rotation, CancellationToken cancellation = default)
    {
        Vector3 environment = Integrate(source, rotation, lighting.ExternalTransmission,
            lighting.HorizonElevation, lighting.CloudCoverage, cancellation);
        float[] aerial = lighting.AerialRadiance.AsSpan().ToArray();
        for (int i = 0; i < aerial.Length; i += 4)
        {
            cancellation.ThrowIfCancellationRequested();
            aerial[i] += environment.X * lighting.AerialAttenuation[i];
            aerial[i+1] += environment.Y * lighting.AerialAttenuation[i+1];
            aerial[i+2] += environment.Z * lighting.AerialAttenuation[i+2];
        }
        return lighting with { CelestialSource = source, CelestialRotation = rotation,
            CelestialEnvironment = environment, Environment = lighting.Environment + environment,
            Horizon = lighting.Horizon + environment, AerialRadiance = ImmutableArray.Create(aerial) };
    }
    #endregion
}
