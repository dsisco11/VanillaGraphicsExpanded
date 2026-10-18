using System;
using Newtonsoft.Json;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Persists bounded cloud quality and the history-free comparison through the existing configuration owner.</summary>
[JsonObject(MemberSerialization.OptIn)]
public sealed class CloudRenderingSettings
{
    /// <summary>Bounds complete-ray primary evaluations independently of native grid crossings.</summary>
    [JsonProperty] public int ViewRaySampleMax { get; set; } = 384;
    /// <summary>Retains a small bounded minimum on short positive-length primary spans.</summary>
    [JsonProperty] public int ViewRaySampleMin { get; set; } = 2;
    /// <summary>Defines the world distance over which the primary count reaches its maximum.</summary>
    [JsonProperty] public float ViewBudgetDistance { get; set; } = 50;
    /// <summary>Bypasses reduced tracing and history for direct full-resolution comparison.</summary>
    [JsonProperty] public bool FullResolutionReference { get; set; }
    /// <summary>Bounds secondary shadow work per occupied native interval.</summary>
    [JsonProperty] public int LightSamples { get; set; } = 1;
    /// <summary>Disables noise and exposed-edge erosion together for native-transmission comparison.</summary>
    [JsonProperty] public bool Detail { get; set; } = true;
    /// <summary>Controls restrained interior density variation without excavating native occupied interiors.</summary>
    [JsonProperty] public float DetailAmplitude { get; set; } = .15f;
    /// <summary>Softens exposed faces inward in world units.</summary>
    [JsonProperty] public float EdgeWidth { get; set; } = 2;
    /// <summary>Controls the periodic material-noise frequency in inverse world units.</summary>
    [JsonProperty] public float NoiseScale { get; set; } = .08f;

    #region Public API
    /// <summary>Bounds persisted input before it controls shader work; nonfinite values restore authored defaults.</summary>
    public void Sanitize()
    {
        ViewRaySampleMax=Math.Clamp(ViewRaySampleMax,4,384);
        ViewRaySampleMin=Math.Clamp(ViewRaySampleMin,1,Math.Min(16,ViewRaySampleMax));
        ViewBudgetDistance=float.IsFinite(ViewBudgetDistance)?Math.Clamp(ViewBudgetDistance,50,10000):50;
        LightSamples=Math.Clamp(LightSamples,1,4);
        DetailAmplitude=float.IsFinite(DetailAmplitude)?Math.Clamp(DetailAmplitude,0,.95f):.15f;
        EdgeWidth=float.IsFinite(EdgeWidth)?Math.Clamp(EdgeWidth,.01f,10):2;
        NoiseScale=float.IsFinite(NoiseScale)?Math.Clamp(NoiseScale,.001f,1):.08f;
    }
    /// <summary>Captures an immutable quality selection shared by reference, tracing and source-range admission.</summary>
    internal CloudVolumeSettings Capture()
    {
        Sanitize();
        return CloudVolumeSettings.Default with { LightSamples=LightSamples,Detail=Detail,
            DetailAmplitude=DetailAmplitude,EdgeWidth=EdgeWidth,NoiseScale=NoiseScale,
            ViewRaySampleMax=ViewRaySampleMax,ViewRaySampleMin=ViewRaySampleMin,ViewBudgetDistance=ViewBudgetDistance };
    }
    #endregion
}
