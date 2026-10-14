using System;
using System.Collections.Immutable;
using System.Numerics;
using System.Threading;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Defines external-source extinction sampling shared by atmospheric production and sky consumers.</summary>
internal static class AtmosphereExternalTransmission
{
    internal const int SampleCount = 512;
    #region Public API
    /// <summary>Matches the draw shader's horizon rejection and linear texture-centre interpolation.</summary>
    internal static Vector3 Sample(ImmutableArray<float> values, float elevation, float horizon)
    {
        if (elevation < horizon) return Vector3.Zero;
        if (values.Length != SampleCount * 4) throw new ArgumentException("Incomplete external transmission.");
        float row = AtmosphereSkyMapping.Coordinate(elevation, horizon) * (SampleCount - 1);
        int low = Math.Clamp((int)row, 0, SampleCount - 1), high = Math.Min(low + 1, SampleCount - 1);
        Vector3 a = new(values[4*low], values[4*low+1], values[4*low+2]);
        Vector3 b = new(values[4*high], values[4*high+1], values[4*high+2]);
        return Vector3.Lerp(a, b, row - low);
    }
    /// <summary>Builds the CPU fallback on the atmosphere worker using the existing extinction integrator.</summary>
    internal static ImmutableArray<float> Build(float altitude, float aerosol, CancellationToken cancellation = default)
    {
        var values = new float[SampleCount*4];
        float horizon = AtmosphereSkyMapping.Horizon(altitude);
        // Radial density makes transmission azimuth independent at this observer.
        // Use the same horizon-focused elevation mapping as the compute producer.
        for (int i = 0; i < SampleCount; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            float elevation = AtmosphereSkyMapping.Elevation((float)i/(SampleCount-1),horizon);
            Vector3 t = AtmosphereModel.ExternalTransmittance(new(MathF.Cos(elevation),MathF.Sin(elevation),0),altitude,aerosol);
            values[4*i] = t.X; values[4*i+1] = t.Y; values[4*i+2] = t.Z; values[4*i+3] = 1;
        }
        return ImmutableArray.Create(values);
    }
    #endregion
}
