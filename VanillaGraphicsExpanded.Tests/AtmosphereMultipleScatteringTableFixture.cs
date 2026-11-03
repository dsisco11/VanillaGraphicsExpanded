using System;
using System.Collections.Generic;
using VanillaGraphicsExpanded.PBR.Atmosphere;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Shares only the three immutable default atmosphere tables needed by one test class.</summary>
public sealed class AtmosphereMultipleScatteringTableFixture : IDisposable
{
    private const int MaximumTables = 3;
    private readonly Dictionary<BuildKey, AtmosphereMultipleScattering> tables = [];

    #region Public API
    /// <summary>Releases the bounded class-local references after all theory rows finish.</summary>
    public void Dispose() => tables.Clear();
    #endregion

    #region Internal API
    /// <summary>Returns a class-owned default table for one of the numerical suite's aerosol rows.</summary>
    internal AtmosphereMultipleScattering GetDefault(float aerosol)
    {
        if (aerosol is not (.1f or 1f or 8f))
            throw new ArgumentOutOfRangeException(nameof(aerosol), "Only the authored numerical-suite aerosol rows are retained.");

        var key = new BuildKey(aerosol, .1f,
            AtmosphereMultipleScattering.DefaultWidth, AtmosphereMultipleScattering.DefaultHeight,
            AtmosphereMultipleScattering.DefaultDirectionSamples, AtmosphereMultipleScattering.DefaultRaySamples,
            AtmosphereMultipleScattering.DefaultLightSamples, UseScalarReference: false);
        if (tables.TryGetValue(key, out var existing)) return existing;
        if (tables.Count >= MaximumTables)
            throw new InvalidOperationException("The atmosphere table fixture is bounded to its three authored aerosol inputs.");

        var table = AtmosphereMultipleScattering.Build(key.Aerosol, groundAlbedo: key.GroundAlbedo,
            sunSamples: key.SunSamples, altitudeSamples: key.AltitudeSamples,
            directionSamples: key.DirectionSamples, raySamples: key.RaySamples,
            lightSamples: key.LightSamples, useScalarReference: key.UseScalarReference);
        tables.Add(key, table);
        return table;
    }
    #endregion

    #region Private
    /// <summary>Identifies every numerical Build input that affects the immutable table value.</summary>
    private readonly record struct BuildKey(float Aerosol, float GroundAlbedo,
        int SunSamples, int AltitudeSamples, int DirectionSamples, int RaySamples, int LightSamples,
        bool UseScalarReference);
    #endregion
}
