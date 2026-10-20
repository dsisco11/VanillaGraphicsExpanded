using System;
using Newtonsoft.Json;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Persists bounded cloud quality and the history-free comparison through the existing configuration owner.</summary>
[JsonObject(MemberSerialization.OptIn)]
public sealed class CloudRenderingSettings
{
    /// <summary>Selects the candidate only by explicit developer choice.</summary>
    [JsonProperty] public CloudVolumeRendererKind VolumeRenderer { get; set; }=CloudVolumeRendererKind.Existing;
    /// <summary>Owns candidate controls separately from existing quality.</summary>
    [JsonProperty] public LightingVolume.LightingVolumeSettings LightingVolume { get; set; }=new();
    /// <summary>Owns stored-density candidate controls independently of other renderers.</summary>
    [JsonProperty] public Volume.StoredVolumeSettings StoredVolume {get;set;}=new();
    /// <summary>Bounds complete-ray primary evaluations independently of native grid crossings.</summary>
    [JsonProperty] public int ViewRaySampleMax { get; set; } = 384;
    /// <summary>Retains a small bounded minimum on short positive-length primary spans.</summary>
    [JsonProperty] public int ViewRaySampleMin { get; set; } = 1;
    /// <summary>Defines the clipped cloud-layer distance at which the primary count reaches its maximum.</summary>
    [JsonProperty] public float ViewBudgetDistance { get; set; } = 10000;
    /// <summary>Selects half or quarter view dimensions for tracing, with half-dimension reconstruction.</summary>
    [JsonProperty] public CloudTraceScale TraceScale { get; set; } = CloudTraceScale.Quarter;
    /// <summary>Bypasses reduced tracing and history for direct full-resolution comparison.</summary>
    [JsonProperty] public bool FullResolutionReference { get; set; }
    /// <summary>Bounds complete-ray secondary samples independently of native grid crossings.</summary>
    [JsonProperty] public int LightRaySampleMax { get; set; } = 10;
    /// <summary>Bounds the shadow span from each contributing primary position in world units.</summary>
    [JsonProperty] public float ShadowDistance { get; set; } = 1500;
    /// <summary>Disables self-shadowing at zero; other levels select 256, 512 and 1024 light-space pixels per axis.</summary>
    [JsonProperty] public int ShadowQuality { get; set; } = 1;
    /// <summary>Stops after an integrated primary step below this transmission; zero evaluates the complete budget.</summary>
    [JsonProperty] public float StopTransmittance { get; set; } = .005f;
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
        if(!Enum.IsDefined(VolumeRenderer)) VolumeRenderer=CloudVolumeRendererKind.Existing;
        LightingVolume ??= new();LightingVolume.Sanitize();
        StoredVolume ??=new();StoredVolume.Sanitize();
        if(TraceScale is not (CloudTraceScale.Half or CloudTraceScale.Quarter)) TraceScale=CloudTraceScale.Quarter;
        ViewRaySampleMax=Math.Clamp(ViewRaySampleMax,4,384);
        ViewRaySampleMin=Math.Clamp(ViewRaySampleMin,1,Math.Min(16,ViewRaySampleMax));
        ViewBudgetDistance=float.IsFinite(ViewBudgetDistance)?Math.Clamp(ViewBudgetDistance,50,10000):10000;
        StopTransmittance=float.IsFinite(StopTransmittance)?Math.Clamp(StopTransmittance,0,.005f):.005f;
        LightRaySampleMax=Math.Clamp(LightRaySampleMax,2,64);
        ShadowQuality=Math.Clamp(ShadowQuality,0,3);
        ShadowDistance=float.IsFinite(ShadowDistance)?Math.Clamp(ShadowDistance,50,10000):1500;
        DetailAmplitude=float.IsFinite(DetailAmplitude)?Math.Clamp(DetailAmplitude,0,.95f):.15f;
        EdgeWidth=float.IsFinite(EdgeWidth)?Math.Clamp(EdgeWidth,.01f,10):2;
        NoiseScale=float.IsFinite(NoiseScale)?Math.Clamp(NoiseScale,.001f,1):.08f;
    }
    /// <summary>Captures an immutable quality selection shared by reference, tracing and source-range admission.</summary>
    internal CloudVolumeSettings Capture()
    {
        Sanitize();
        return CloudVolumeSettings.Default with { LightRaySampleMax=LightRaySampleMax,ShadowDistance=ShadowDistance,StopTransmittance=StopTransmittance,TraceScale=TraceScale,Detail=Detail,
            DetailAmplitude=DetailAmplitude,EdgeWidth=EdgeWidth,NoiseScale=NoiseScale,ShadowQuality=ShadowQuality,
            ViewRaySampleMax=ViewRaySampleMax,ViewRaySampleMin=ViewRaySampleMin,ViewBudgetDistance=ViewBudgetDistance };
    }
    #endregion
}
