using System;
using Newtonsoft.Json;

namespace VanillaGraphicsExpanded.PBR.Tessellation;

/// <summary>Bounded screen-space subdivision and physical-distance displacement fade settings.</summary>
[JsonObject(MemberSerialization.OptIn)]
public sealed class TerrainSubdivisionSettings
{
    [JsonProperty] public int MaximumLevel { get; set; } = 8;
    [JsonProperty] public float TargetEdgePixels { get; set; } = 16;
    [JsonProperty] public float FadeStartMetres { get; set; } = 8;
    [JsonProperty] public float FadeEndMetres { get; set; } = 24;

    #region Validation
    /// <summary>Normalizes hostile/non-finite configuration before uniform publication.</summary>
    public void Sanitize()
    {
        MaximumLevel = Math.Clamp(MaximumLevel, 1, 8);
        TargetEdgePixels = float.IsFinite(TargetEdgePixels) ? Math.Clamp(TargetEdgePixels, 2, 256) : 16;
        FadeStartMetres = float.IsFinite(FadeStartMetres) ? Math.Clamp(FadeStartMetres, 0, 127) : 8;
        FadeEndMetres = float.IsFinite(FadeEndMetres) ? Math.Clamp(FadeEndMetres, FadeStartMetres + 1, 128) : Math.Max(24, FadeStartMetres + 1);
    }
    #endregion
}
