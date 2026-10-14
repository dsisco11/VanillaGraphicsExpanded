using System;
namespace VanillaGraphicsExpanded.PBR.Postprocessing.LocalExposure;
/// <summary>Persists an optional luminance-driven display candidate independently of source illumination.</summary>
public sealed class LocalExposureSettings
{
    #region Public API
    /// <summary>Enables local exposure by default while retaining explicit saved choices.</summary>
    public bool Enabled { get; set; }=true;
    /// <summary>Bounds the horizontal base grid.</summary>
    public int Width { get; set; }=256;
    /// <summary>Bounds the vertical base grid.</summary>
    public int Height { get; set; }=144;
    /// <summary>Sets bilateral similarity width in physical luminance stops.</summary>
    public float RangeWidth { get; set; }=1;
    /// <summary>Retains this fraction of broad shadow contrast; one is identity.</summary>
    public float ShadowStrength { get; set; }=.5f;
    /// <summary>Retains this fraction of broad highlight contrast; one is identity.</summary>
    public float HighlightStrength { get; set; }=.5f;
    /// <summary>Limits local shadow brightening without altering the camera exposure cap.</summary>
    public float ShadowLimit { get; set; }=4;
    /// <summary>Limits local highlight darkening.</summary>
    public float HighlightLimit { get; set; }=4;
    /// <summary>Displays red brightening, blue darkening and neutral gray instead of graded scene color.</summary>
    public bool Visualize { get; set; }
    /// <summary>Produces a finite immutable candidate without rewriting persisted values.</summary>
    internal LocalExposureParameters Snapshot()=>new(Enabled,Math.Clamp(Width,1,512),Math.Clamp(Height,1,512),
        Finite(RangeWidth,1,.125f,8),Finite(ShadowStrength,.5f,0,1),Finite(HighlightStrength,.5f,0,1),
        Finite(ShadowLimit,4,0,8),Finite(HighlightLimit,4,0,8),Visualize);
    #endregion
    #region Private
    /// <summary>Rejects unsupported configuration arithmetic before GPU publication.</summary>
    private static float Finite(float value,float fallback,float low,float high)=>Math.Clamp(float.IsFinite(value)?value:fallback,low,high);
    #endregion
}
