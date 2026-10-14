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
