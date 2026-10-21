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
    /// <summary>Stops after a completed view segment; zero compiles termination out.</summary>
    [JsonProperty] public float StopTransmittance {get;set;}=.005f;

    /// <summary>Bounds direct plus diffuse rays per lighting update in complete block-sized work units.</summary>
    [JsonProperty] public int LightingRaysPerUpdate {get;set;}=CloudVolumeLightingBudget.DefaultRays;

    #region Public API
    /// <summary>Bounds persisted work controls before publishing a coherent settings snapshot.</summary>
    public void Sanitize()
    {
        LightingRaysPerUpdate=CloudVolumeLightingBudget.Sanitize(LightingRaysPerUpdate);
        if(TraceScale is not (CloudTraceScale.Half or CloudTraceScale.Quarter))TraceScale=CloudTraceScale.Quarter;
        StopTransmittance=float.IsFinite(StopTransmittance)?Math.Clamp(StopTransmittance,0,.005f):.005f;
    }
    /// <summary>Freezes features and resolution together for the early render boundary.</summary>
    internal CloudVolumeViewSettings Capture()
    {
        Sanitize();
        return new(Detail,SelfShadowing,Diffuse,StopTransmittance,FullResolutionReference?1:(int)TraceScale,LightingRaysPerUpdate);
    }
    #endregion
}
