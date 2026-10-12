using System;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>Retains physical component bounds so source removal cannot discard brighter compatible history.</summary>
internal readonly record struct FrameLightingBounds(double Surface, double WaterReflection,
    double BoundaryScattering, double SurfaceScattering, double AirSky, double Additive)
{
    #region Public API
    /// <summary>Assembles the qualified nonrecursive scene topology from published lux, candela and nits maxima.</summary>
    internal static FrameLightingBounds FromSources(double solarLux, double diskNits, double summedPointCandela,
        double environmentEOverPi, double blockLux, double indirectNits, double emissionNits, double aerialNits, double additiveNits)
    {
        Validate(solarLux); Validate(diskNits); Validate(summedPointCandela); Validate(environmentEOverPi);
        Validate(blockLux); Validate(indirectNits); Validate(emissionNits); Validate(aerialNits); Validate(additiveNits);
        // Use the qualified finite-disk/punctual response bounds and existing nonrecursive
        // water/air topology. Producer rounding allowances enter through the supplied maxima.
        double diffuse = (blockLux + Math.PI * environmentEOverPi) / (4 * Math.PI);
        return new(.9 * diskNits + (66560 + 1 / Math.PI) * summedPointCandela + 2 * solarLux / Math.PI + indirectNits + emissionNits,
            2 * diskNits + 200000 * summedPointCandela + environmentEOverPi,
            64 * solarLux + diffuse, (2 / .0722) * (64 * (solarLux + summedPointCandela) + diffuse),
            3 * aerialNits, additiveNits);
    }
    /// <summary>Combines originating and current bounds per component before summing the frame.</summary>
    internal FrameLightingBounds IncludeHistory(FrameLightingBounds history)
    {
        _ = Total; _ = history.Total;
        // Old indirect and new direct sources can coexist; a maximum of whole-frame sums loses that combination.
        return new(Math.Max(Surface, history.Surface), Math.Max(WaterReflection, history.WaterReflection),
            Math.Max(BoundaryScattering, history.BoundaryScattering), Math.Max(SurfaceScattering, history.SurfaceScattering),
            Math.Max(AirSky, history.AirSky), Math.Max(Additive, history.Additive));
    }
    /// <summary>Publishes an outward bound; callers include producer arithmetic allowances in the component metadata.</summary>
    internal float Total
    {
        get
        {
            Validate(Surface); Validate(WaterReflection); Validate(BoundaryScattering);
            Validate(SurfaceScattering); Validate(AirSky); Validate(Additive);
            return LightingStorage.UpperBound(Surface + WaterReflection + BoundaryScattering + SurfaceScattering + AirSky + Additive);
        }
    }
    #endregion
    #region Private
    /// <summary>Rejects missing arithmetic domains rather than manufacturing a valid black bound.</summary>
    private static void Validate(double value)
    {
        if (!double.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value));
    }
    #endregion
}
