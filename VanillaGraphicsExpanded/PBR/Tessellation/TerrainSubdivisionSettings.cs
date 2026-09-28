using System;
using Newtonsoft.Json;

namespace VanillaGraphicsExpanded.PBR.Tessellation;

/// <summary>Bounded screen-space subdivision and physical-distance displacement fade settings.</summary>
[JsonObject(MemberSerialization.OptIn)]
public sealed class TerrainSubdivisionSettings
{
    #region Defaults
    private const int DefaultSubdivisionLevel = 5;
    private const float DefaultTargetEdgePixels = 8;
    private const float DefaultFadeStartMetres = 4;
    private const float DefaultFadeEndMetres = 16;
    #endregion

    #region Constants
    private const int MinSubdivisionLevel = 1;
    private const int MaxSubdivisionLevel = 8;
    private const float MinTargetEdgePixels = 2;
    private const float MaxTargetEdgePixels = 256;
    private const float MinFadeStartMetres = 1;
    private const float MaxFadeStartMetres = 127;
    private const float MaxFadeEndMetres = 128;
    #endregion

    /// <summary>Maximum subdivision level, bounded from one to eight.</summary>
    [JsonProperty]
    public int MaximumLevel { get; set; } = DefaultSubdivisionLevel;
    [JsonProperty] public float TargetEdgePixels { get; set; } = DefaultTargetEdgePixels;
    [JsonProperty] public float FadeStartMetres { get; set; } = DefaultFadeStartMetres;
    [JsonProperty] public float FadeEndMetres { get; set; } = DefaultFadeEndMetres;
    #region Validation
    /// <summary>Normalizes hostile/non-finite configuration before uniform publication.</summary>
    public void Sanitize()
    {
        MaximumLevel = Math.Clamp(MaximumLevel, MinSubdivisionLevel, MaxSubdivisionLevel);
        TargetEdgePixels = float.IsFinite(TargetEdgePixels) ? Math.Clamp(TargetEdgePixels, MinTargetEdgePixels, MaxTargetEdgePixels) : DefaultTargetEdgePixels;
        FadeStartMetres = float.IsFinite(FadeStartMetres) ? Math.Clamp(FadeStartMetres, MinFadeStartMetres, MaxFadeStartMetres) : DefaultFadeStartMetres;
        FadeEndMetres = float.IsFinite(FadeEndMetres) ? Math.Clamp(FadeEndMetres, FadeStartMetres + 1, MaxFadeEndMetres) : Math.Clamp(DefaultFadeEndMetres, FadeStartMetres + 1, MaxFadeEndMetres);
    }
    #endregion
}
