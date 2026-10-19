using System;
using Newtonsoft.Json;

namespace VanillaGraphicsExpanded.PBR.Clouds.LightingVolume;

/// <summary>Persists independent view controls while transport and work budgets remain fixed.</summary>
[JsonObject(MemberSerialization.OptIn)]
public sealed class LightingVolumeSettings
{
    /// <summary>Caps deterministic view midpoints.</summary>
    [JsonProperty] public int ViewMaximum { get; set; }=96;
    /// <summary>Defines the span receiving the full view budget.</summary>
    [JsonProperty] public float ViewBudgetDistance { get; set; }=10000;
    /// <summary>Stops after a completed step; zero compiles termination out.</summary>
    [JsonProperty] public float StopTransmittance { get; set; }=.005f;
    /// <summary>Compiles density modulation and edge erosion.</summary>
    [JsonProperty] public bool Detail { get; set; }=true;
    /// <summary>Compiles transmission lookup and enables direct jobs.</summary>
    [JsonProperty] public bool SelfShadowing { get; set; }=true;
    /// <summary>Compiles diffuse lookup and enables scattering jobs.</summary>
    [JsonProperty] public bool Diffuse { get; set; }=true;
    #region Public API
    /// <summary>Bounds persisted inputs independently of existing renderer quality.</summary>
    public void Sanitize()
    {
        ViewMaximum=Math.Clamp(ViewMaximum,4,96);
        ViewBudgetDistance=float.IsFinite(ViewBudgetDistance)?Math.Clamp(ViewBudgetDistance,50,10000):10000;
        StopTransmittance=float.IsFinite(StopTransmittance)?Math.Clamp(StopTransmittance,0,.005f):.005f;
    }
    /// <summary>Captures immutable candidate controls at applied-configuration notification.</summary>
    internal LightingVolumeViewSettings Capture()
    {
        Sanitize();return new(ViewMaximum,ViewBudgetDistance,StopTransmittance,Detail,SelfShadowing,Diffuse);
    }
    #endregion
}
