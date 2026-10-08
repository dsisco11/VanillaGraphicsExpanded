using System;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Persists bounded glare controls independently of camera exposure and lighting.</summary>
public sealed class PostprocessSettings
{
    #region Public API
    /// <summary>Sets linear bloom contribution strength.</summary>
    public float BloomStrength { get; set; }=.08f;
    /// <summary>Sets the bloom threshold in exposed linear scene units.</summary>
    public float BloomThreshold { get; set; }=1;
    /// <summary>Sets the fractional width of the soft threshold.</summary>
    public float BloomKnee { get; set; }=.5f;
    /// <summary>Bounds the downsample pyramid between three and six levels.</summary>
    public int BloomLevels { get; set; }=5;
    /// <summary>Scales the atmosphere's solar irradiance for the authored screen-space glare.</summary>
    public float GodRayStrength { get; set; }=.25f;
    /// <summary>Bounds shaft radiance before exposure, without applying a display transfer.</summary>
    public float GodRayLimit { get; set; }=.5f;
    /// <summary>Selects 16, 32 or 64 radial visibility samples.</summary>
    public int GodRaySamples { get; set; }=32;
    /// <summary>Produces finite bounded parameters without mutating persisted UI values.</summary>
    internal PostprocessParameters Snapshot() => new(Finite(BloomStrength,.08f,0,2),Finite(BloomThreshold,1,.01f,16),
        Finite(BloomKnee,.5f,0,1),Math.Clamp(BloomLevels,3,6),Finite(GodRayStrength,.25f,0,2),
        Finite(GodRayLimit,.5f,0,4),GodRaySamples<=16?16:GodRaySamples<=32?32:64);
    #endregion
    #region Private
    /// <summary>Rejects invalid persisted arithmetic before GPU publication.</summary>
    private static float Finite(float value,float fallback,float min,float max)=>Math.Clamp(float.IsFinite(value)?value:fallback,min,max);
    #endregion
}
