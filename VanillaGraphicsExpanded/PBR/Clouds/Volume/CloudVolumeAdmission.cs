using System;
using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Retains physical source headroom before scene encoding freezes, independently of later native density publication.</summary>
internal sealed class CloudVolumeAdmission
{
    internal CloudVolumeLightingBounds? Bounds {get;private set;}
    internal CloudVolumeEncoding Encoding=>Bounds?.Encoding??new(Vector3.Zero);
    #region Public API
    /// <summary>Includes retained cloud energy, current atmosphere and local-medium radiance in frame admission.</summary>
    internal float Observe(AtmosphereLighting light,float localFogMaximum=0)
    {
        if(!float.IsFinite(localFogMaximum)||localFogMaximum<0||!float.IsFinite(light.ReconstructionMaximumNits)||light.ReconstructionMaximumNits<0)
            throw new ArgumentOutOfRangeException(nameof(localFogMaximum));
        var bounds=CloudVolumeLightingBounds.Admit(light,Bounds,Encoding);
        float maximum=Maximum(bounds,light.ReconstructionMaximumNits,localFogMaximum);
        Bounds=bounds;return maximum;
    }
    /// <summary>Reserves a finite float32 accumulation allowance around the maximum physical source.</summary>
    internal static float Maximum(CloudVolumeLightingBounds bounds,float aerial,float fog)
    {
        double q=Math.Max(bounds.DirectCap.X,Math.Max(bounds.DirectCap.Y,bounds.DirectCap.Z));
        double d=Math.Max(bounds.Encoding.Cap.X,Math.Max(bounds.Encoding.Cap.Y,bounds.Encoding.Cap.Z));
        return LightingStorage.UpperBound((q+d+aerial+fog)*(1+2048.0/(1<<23)));
    }
    #endregion
}
