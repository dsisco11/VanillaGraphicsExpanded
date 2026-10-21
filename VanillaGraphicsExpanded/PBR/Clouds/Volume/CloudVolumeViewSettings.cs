using System;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Freezes feature choices for a single stored-volume consumer lifetime.</summary>
internal readonly record struct CloudVolumeViewSettings(bool Detail,bool SelfShadowing,bool Diffuse,float Stop,int TraceDivisor=4,int LightingRaysPerUpdate=CloudVolumeLightingBudget.DefaultRays)
{
    #region Public API
    /// <summary>Provides the selected deterministic view contract.</summary>
    internal static CloudVolumeViewSettings Default=>new(true,true,true,.005f);
    /// <summary>Rejects unsupported optical stopping rules before mutating executables.</summary>
    internal void Validate()
    {
        if(!float.IsFinite(Stop)||Stop is <0 or >.005f)throw new ArgumentOutOfRangeException(nameof(Stop));
        if(LightingRaysPerUpdate!=CloudVolumeLightingBudget.Sanitize(LightingRaysPerUpdate))throw new ArgumentOutOfRangeException(nameof(LightingRaysPerUpdate));
        if(TraceDivisor is not (1 or 2 or 4))throw new ArgumentOutOfRangeException(nameof(TraceDivisor));
    }
    #endregion
}
