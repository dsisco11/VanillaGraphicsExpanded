using System;
using System.Numerics;

namespace VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;

/// <summary>Admits a procedural lunar disk whose projected radiance integrates to the published lunar illuminance.</summary>
internal readonly record struct MoonDisk(Vector3 Direction, Vector3 Right, Vector3 Up,
    Vector3 SurfaceLight, Vector3 Radiance, float AngularRadius, float Horizon)
{
    #region Public API
    /// <summary>Bounds the Lambert surface response without depending on a shader compiler's evaluation order.</summary>
    internal float MaximumNits => LightingStorage.UpperBound(
        (double)Math.Max(Radiance.X, Math.Max(Radiance.Y, Radiance.Z)) * 1.01);

    /// <summary>Captures phase, orientation, horizon and extinguished power from one completed atmosphere generation.</summary>
    internal static MoonDisk Capture(AtmosphereLighting lighting)
    {
        if (!LightingCalibration.FiniteNonnegative(lighting.Lunar)
            || !float.IsFinite(lighting.MoonPhase) || !float.IsFinite(lighting.Moon.LengthSquared())
            || Math.Abs(lighting.Moon.LengthSquared()-1) > .001f
            || !float.IsFinite(lighting.MoonAngularRadius) || lighting.MoonAngularRadius <= 0
            || lighting.MoonAngularRadius > .1f || !float.IsFinite(lighting.HorizonElevation))
            throw new ArgumentException("Lunar disk requires finite completed physical inputs.");
        Vector3 direction = lighting.Moon;
        Vector3 tangent = lighting.Sun-direction*Vector3.Dot(lighting.Sun,direction);
        if (!float.IsFinite(tangent.LengthSquared()) || tangent.LengthSquared() < 1e-8f)
            tangent = Vector3.Cross(Math.Abs(direction.Y)<.9f ? Vector3.UnitY : Vector3.UnitX,direction);
        Vector3 right = Vector3.Normalize(tangent), up = Vector3.Normalize(Vector3.Cross(direction,right));
        float phase = (lighting.MoonPhase%8+8)%8;
        double angle = Math.Abs(phase-4)*Math.PI/4;
        Vector3 light = new((float)Math.Sin(angle),0,(float)Math.Cos(angle));
        var disk = new MoonDisk(direction,right,up,light,Vector3.Zero,lighting.MoonAngularRadius,lighting.HorizonElevation);
        if (!lighting.HasNightSources || lighting.Lunar == Vector3.Zero) return disk;
        double integral = ProjectedIntegral(disk,angle);
        if (!(integral > 0)) throw new ArgumentException("Nonzero lunar power requires a visible illuminated disk.");
        // Lunar already includes atmospheric extinction and horizon admission. Do not attenuate it a second time.
        Vector3 radiance = new((float)(lighting.Lunar.X/integral),
            (float)(lighting.Lunar.Y/integral),(float)(lighting.Lunar.Z/integral));
        if (!LightingCalibration.FiniteNonnegative(radiance)) throw new ArgumentException("Invalid lunar radiance.");
        return disk with { Radiance = radiance };
    }
    #endregion
    #region Private
    /// <summary>Integrates projected Lambert area analytically when unobstructed and by bounded quadrature at the horizon.</summary>
    private static double ProjectedIntegral(MoonDisk disk, double angle)
    {
        double sine = Math.Sin(disk.AngularRadius), elevation = Math.Asin(Math.Clamp(disk.Direction.Y,-1,1));
        if (elevation-disk.AngularRadius >= disk.Horizon)
        {
            double crescent=Math.PI-angle;
            // Near new moon the ordinary sine/cosine expression subtracts almost equal values.
            double area=crescent<.01 ? crescent*crescent*crescent*(1.0/3-crescent*crescent/30)
                : Math.Sin(angle)+crescent*Math.Cos(angle);
            return sine*sine*(2.0/3)*area;
        }
        if (elevation+disk.AngularRadius <= disk.Horizon) return 0;
        // Uniform projected-area samples resolve the overlap of terminator and horizon.
        // This runs only for a new publication; drawing never performs this integration.
        const int rings=64, sectors=256;
        double sum=0, horizonY=Math.Sin(disk.Horizon);
        for(int ring=0;ring<rings;ring++)
        {
            double radius=Math.Sqrt((ring+.5)/rings), z=Math.Sqrt(1-radius*radius);
            for(int sector=0;sector<sectors;sector++)
            {
                double azimuth=2*Math.PI*(sector+.5)/sectors;
                double x=radius*Math.Cos(azimuth), y=radius*Math.Sin(azimuth);
                double worldY=disk.Direction.Y*Math.Sqrt(1-sine*sine*radius*radius)
                    +sine*(disk.Right.Y*x+disk.Up.Y*y);
                if(worldY>=horizonY) sum+=Math.Max(0,x*disk.SurfaceLight.X+z*disk.SurfaceLight.Z);
            }
        }
        return sine*sine*Math.PI*sum/(rings*sectors);
    }
    #endregion
}
