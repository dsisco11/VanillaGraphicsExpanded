using System;
using Newtonsoft.Json;
using VanillaGraphicsExpanded.PBR.Clouds.Volume;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Persists production cloud features and view resolution through the configuration owner.</summary>
[JsonObject(MemberSerialization.OptIn)]
public sealed class CloudRenderingSettings
{
    /// <summary>Selects half or quarter view dimensions before temporal spatial sampling.</summary>
    [JsonProperty] public CloudTraceScale TraceScale {get;set;}=CloudTraceScale.Quarter;
    /// <summary>Bypasses reduced tracing and history for direct full-resolution comparison.</summary>
    [JsonProperty] public bool FullResolutionReference {get;set;}
    /// <summary>Compiles density shaping and erosion during stored-density generation.</summary>
    [JsonProperty] public bool Detail {get;set;}=true;
    /// <summary>Compiles cached direct visibility and enables direct transport jobs.</summary>
    [JsonProperty] public bool SelfShadowing {get;set;}=true;
    /// <summary>Compiles retained diffuse sampling and enables progressive gather jobs.</summary>
    [JsonProperty] public bool Diffuse {get;set;}=true;
    /// <summary>Selects a coherent compiled workload independently of trace resolution.</summary>
    [JsonProperty] public int Quality {get;set;}=2;

    /// <summary>Overrides the profile ray limit; zero selects the profile's default budget.</summary>
    [JsonProperty] public int LightingRaysPerUpdate {get;set;}

    #region Public API
    /// <summary>Bounds persisted work controls before publishing a coherent settings snapshot.</summary>
    public void Sanitize()
    {
        Quality=Math.Clamp(Quality,0,4);
        LightingRaysPerUpdate=LightingRaysPerUpdate==0?0:CloudVolumeLightingBudget.Sanitize(LightingRaysPerUpdate);
        if(TraceScale is not (CloudTraceScale.Half or CloudTraceScale.Quarter))TraceScale=CloudTraceScale.Quarter;

    }
    /// <summary>Freezes features and resolution together for the early render boundary.</summary>
    internal CloudVolumeViewSettings Capture()
    {
        Sanitize();
        var profile=CloudVolumeQuality.Resolve(Quality);
        return new(Detail,SelfShadowing,Diffuse,profile.ViewStop,FullResolutionReference?1:(int)TraceScale,
            LightingRaysPerUpdate==0?profile.LightingRays:LightingRaysPerUpdate,Quality);
    }
    #endregion
}
