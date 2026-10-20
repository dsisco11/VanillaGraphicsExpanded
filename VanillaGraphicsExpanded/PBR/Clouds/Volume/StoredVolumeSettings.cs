using System;
using Newtonsoft.Json;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Persists stored-volume feature choices without exposing experiment lattice or solver budgets.</summary>
[JsonObject(MemberSerialization.OptIn)]
public sealed class StoredVolumeSettings
{
    /// <summary>Compiles generation-only shaping and erosion.</summary>
    [JsonProperty] public bool Detail {get;set;}=true;
    /// <summary>Compiles cached direct visibility and enables direct jobs.</summary>
    [JsonProperty] public bool SelfShadowing {get;set;}=true;
    /// <summary>Compiles retained diffuse sampling and enables progressive gather jobs.</summary>
    [JsonProperty] public bool Diffuse {get;set;}=true;
    /// <summary>Stops after a completed segment; zero compiles termination out.</summary>
    [JsonProperty] public float StopTransmittance {get;set;}=.005f;
    #region Public API
    /// <summary>Restores unsupported optical exits before configuration is published.</summary>
    public void Sanitize()=>StopTransmittance=float.IsFinite(StopTransmittance)?Math.Clamp(StopTransmittance,0,.005f):.005f;
    /// <summary>Freezes stored-field features before selection applies the shared resolution controls.</summary>
    internal CloudVolumeViewSettings Capture(){Sanitize();return new(Detail,SelfShadowing,Diffuse,StopTransmittance);}
    #endregion
}

