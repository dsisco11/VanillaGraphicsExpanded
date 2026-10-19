using System;
using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere;

namespace VanillaGraphicsExpanded.PBR.Clouds.LightingVolume;

/// <summary>Retains exposure-independent source envelopes until all associated cloud lighting is cleared.</summary>
internal sealed class LightingVolumeBounds
{
    private Vector3 solar,lunar,environment;
    internal float SourceCap { get; private set; }
    internal LightingVolumeEncoding Encoding { get; private set; }=LightingVolumeEncoding.Create(0);

    #region Public API
    /// <summary>Admits a completed physical publication before scene storage freezes, retaining earlier brighter sources.</summary>
    internal float Observe(AtmosphereLighting lighting,float localFogMaximum=0)
    {
        Validate(lighting.Solar);Validate(lighting.Lunar);Validate(lighting.Environment);
        if(!float.IsFinite(localFogMaximum)||localFogMaximum<0 ||
            !float.IsFinite(lighting.ReconstructionMaximumNits)||lighting.ReconstructionMaximumNits<0)
            throw new ArgumentOutOfRangeException(nameof(localFogMaximum));
        var s=Vector3.Max(solar,lighting.Solar);var l=Vector3.Max(lunar,lighting.Lunar);
        var e=Vector3.Max(environment,lighting.Environment);
        const double albedo=.9,g=.6;
        double phase=(1+g)/(4*Math.PI*(1-g)*(1-g));
        double q=albedo*phase*((double)Peak(s)+Peak(l));
        double d=albedo*(q+Peak(e))/(1-albedo);
        // Outward caps include CPU rounding; reserve margin for the finite float32 accumulation path.
        float qcap=LightingStorage.UpperBound(q),dcap=LightingStorage.UpperBound(d);
        var encoding=Encoding.Grow(dcap);
        float maximum=LightingStorage.UpperBound(((double)qcap+dcap+lighting.ReconstructionMaximumNits+localFogMaximum)*
            (1+16384.0/(1<<23)));
        solar=s;lunar=l;environment=e;SourceCap=qcap;Encoding=encoding;
        return maximum;
    }
    /// <summary>Allows envelopes to shrink only when the owning lighting solution is cleared.</summary>
    internal void Reset()
    {solar=lunar=environment=Vector3.Zero;SourceCap=0;Encoding=LightingVolumeEncoding.Create(0);}
    #endregion
    #region Private
    /// <summary>Rejects negative or nonfinite physical RGB before changing any retained envelope.</summary>
    private static void Validate(Vector3 value)
    {
        if(!float.IsFinite(value.X)||!float.IsFinite(value.Y)||!float.IsFinite(value.Z)||
            value.X<0||value.Y<0||value.Z<0) throw new ArgumentOutOfRangeException(nameof(value));
    }
    /// <summary>Extracts a conservative scalar envelope from nonnegative physical RGB.</summary>
    private static float Peak(Vector3 value)=>Math.Max(value.X,Math.Max(value.Y,value.Z));
    #endregion
}
