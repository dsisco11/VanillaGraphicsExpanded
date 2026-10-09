using System;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Persists bounded glare controls independently of camera exposure and lighting.</summary>
public sealed class PostprocessSettings
{
    #region Public API
    /// <summary>Sets linear bloom contribution strength.</summary>
    public float BloomStrength { get; set; }=.08f;
    /// <summary>Sets the bloom threshold in exposed linear scene units; zero bypasses highlight selection.</summary>
    public float BloomThreshold { get; set; }=1;
    /// <summary>Sets the fractional width of the soft threshold.</summary>
    public float BloomKnee { get; set; }=.5f;
    /// <summary>Bounds the downsample pyramid between three and six levels.</summary>
    public int BloomLevels { get; set; }=5;
    /// <summary>Scales exposure-relative HDR extraction for light-shaft bloom.</summary>
    public float LightShaftStrength { get; set; }=.25f;
    /// <summary>Bounds exposed peak shaft radiance while preserving unexposed storage.</summary>
    public float LightShaftLimit { get; set; }=.5f;
    /// <summary>Caps radial work at 16, 32 or 64 samples per pass under native quality.</summary>
    public int LightShaftSamples { get; set; }=32;
    /// <summary>Produces finite bounded parameters without mutating persisted UI values.</summary>
    internal PostprocessParameters Snapshot() => new(Finite(BloomStrength,.08f,0,2),Finite(BloomThreshold,1,0,16),
        Finite(BloomKnee,.5f,0,1),Math.Clamp(BloomLevels,3,6),Finite(LightShaftStrength,.25f,0,2),
        Finite(LightShaftLimit,.5f,0,4),LightShaftSamples<=16?16:LightShaftSamples<=32?32:64);
    #endregion
    #region Private
    /// <summary>Rejects invalid persisted arithmetic before GPU publication.</summary>
    private static float Finite(float value,float fallback,float min,float max)=>Math.Clamp(float.IsFinite(value)?value:fallback,min,max);
    #endregion
}
