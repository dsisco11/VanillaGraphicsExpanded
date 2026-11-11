using Newtonsoft.Json;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Configuration;

/// <summary>Owns bounded shadow input and candidate policies; rendering admission is not yet available.</summary>
[JsonObject(MemberSerialization.OptIn)]
public sealed class ShadowSettings
{
    /// <summary>Configurable bounded native publication allowances.</summary>
    [JsonProperty] public ShadowInputSettings Inputs { get; set; } = new();
    /// <summary>Configurable GPU association storage, continuation and aggregate work allowances.</summary>
    [JsonProperty] public ShadowCandidateSettings Candidates { get; set; } = new();
    /// <summary>Configurable physical depth and page metadata allowances.</summary>
    [JsonProperty] public ShadowPageSettings Pages { get; set; } = new();
    /// <summary>Configurable whole-batch scene preparation, selection and drawing allowances.</summary>
    [JsonProperty] public ShadowDrawSettings Drawing { get; set; } = new();
    /// <summary>Configurable age and source-motion limits for complete retained depths.</summary>
    [JsonProperty] public ShadowTemporalSettings Temporal { get; set; } = new();

    #region Public API
    /// <summary>Materializes finite input and candidate policies under the existing configuration owner.</summary>
    internal void Sanitize()
    {
        Inputs ??= new();
        Inputs.Sanitize();
        Candidates ??= new();
        Candidates.Sanitize();
        Pages ??= new();
        Pages.Sanitize();
        Drawing ??= new();
        Drawing.Sanitize();
        Temporal ??= new();
        Temporal.Sanitize();
    }
    #endregion
}
