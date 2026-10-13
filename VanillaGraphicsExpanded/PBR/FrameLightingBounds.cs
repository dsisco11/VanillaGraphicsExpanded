using System;
using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>Retains physical component bounds so source removal cannot discard brighter compatible history.</summary>
internal readonly record struct FrameLightingBounds(double Surface, double WaterReflection,
    double BoundaryScattering, double SurfaceScattering, double AirSky, double Additive)
{
    #region Public API
    /// <summary>Assembles actual completed source publications with admitted indirect and compatibility contributions.</summary>
    internal static FrameLightingBounds FromPublications(AtmosphereLighting atmosphere, VgeLightsUniformBuffer lights,
        LightingCalibration calibration, bool lumon, float indirectIntensity, Vector3 indirectTint,
        float emissionMaximumNits, float compatibilityMaximumNits)
    {
        ArgumentNullException.ThrowIfNull(atmosphere);
        ArgumentNullException.ThrowIfNull(lights);
        calibration.Validate();
        Validate(indirectIntensity); Validate(emissionMaximumNits); Validate(compatibilityMaximumNits);
        if (!LightingCalibration.FiniteNonnegative(atmosphere.Solar)
            || !LightingCalibration.FiniteNonnegative(atmosphere.Lunar)
            || !LightingCalibration.FiniteNonnegative(atmosphere.Environment)
            || !LightingCalibration.FiniteNonnegative(indirectTint)
            || atmosphere.CalibrationGeneration == 0 || atmosphere.LookupStorageScale != LightingStorage.PersistentScale)
            throw new ArgumentException("Frame bounds require matching admitted physical source publications.");
        Vector3 disk = CelestialDiskBound(atmosphere);
        if (!LightingCalibration.FiniteNonnegative(disk))
            throw new ArgumentException("The completed atmosphere has unsupported disk radiance.", nameof(atmosphere));
        // Both engine decoders preserve a unit normalized field. Its positive toe can
        // only reduce the actual calibrated endpoint; include either receiver route.
        double terrainBlock = calibration.BlockReferenceLux * (1.0 - calibration.BlockNeutralFloor);
        double accessorBlock = calibration.AccessorBlockReferenceLux > 0
            ? calibration.AccessorBlockReferenceLux * (1.0 - calibration.AccessorBlockNeutralFloor) : terrainBlock;
        double block = Math.Max(terrainBlock, accessorBlock);
        double environment = Peak(atmosphere.Environment);
        // Admitted directional probes bound reconstructed SH9 E/pi. Artistic gain/tint
        // enters once at gather; forward receivers instead retain their local block fallback.
        double indirect = lumon
            ? Math.Max(block / Math.PI, (1.28 * LightingStorage.DirectionalMaximum + 9.36161e-7)
                * indirectIntensity * Peak(indirectTint))
            : environment + block / Math.PI;
        const double arithmeticGuard = 1 + 256.0 / (1 << 23);
        return FromSources(Peak(atmosphere.DirectIrradiance) * arithmeticGuard, Peak(disk) * arithmeticGuard,
            lights.SummedCandelaMaximum, environment * arithmeticGuard, block * arithmeticGuard,
            indirect * arithmeticGuard, emissionMaximumNits * arithmeticGuard,
            atmosphere.ReconstructionMaximumNits, compatibilityMaximumNits);
    }
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
    /// <summary>Bounds the visible solar raster and the selected finite lunar emitter's specular response.</summary>
    private static Vector3 CelestialDiskBound(AtmosphereLighting atmosphere)
    {
        Vector3 disk = AtmosphereSolarDisk.Radiance(atmosphere);
        if (!atmosphere.MoonIsDirect) return disk;
        float radius = atmosphere.MoonAngularRadius;
        if (!float.IsFinite(radius) || radius < AtmosphereSolarDisk.AngularRadius || radius > .04f)
            throw new ArgumentException("Lunar source radius is outside its admitted response domain.", nameof(atmosphere));
        float visible = AtmosphereSolarDisk.Visibility(MathF.Asin(Math.Clamp(atmosphere.Moon.Y, -1, 1)),
            atmosphere.HorizonElevation, radius);
        if (visible <= 0) throw new ArgumentException("Lunar illuminance requires a visible emitter.", nameof(atmosphere));
        // This equivalent emitter radiance bounds finite-disk reflections. It does not
        // recalibrate the separately rendered compatibility moon sprite.
        float sine = MathF.Sin(radius);
        return Vector3.Max(disk, atmosphere.Lunar / (visible * MathF.PI * sine * sine));
    }
    /// <summary>Extracts a component bound without treating RGB luminance as a storage-component maximum.</summary>
    private static double Peak(Vector3 value) => Math.Max(value.X, Math.Max(value.Y, value.Z));
    /// <summary>Rejects missing arithmetic domains rather than manufacturing a valid black bound.</summary>
    private static void Validate(double value)
    {
        if (!double.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value));
    }
    #endregion
}
