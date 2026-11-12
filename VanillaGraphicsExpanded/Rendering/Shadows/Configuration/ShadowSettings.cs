using Newtonsoft.Json;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Configuration;

/// <summary>Owns bounded shadow publication, storage and whole-frame admission policies.</summary>
[JsonObject(MemberSerialization.OptIn)]
public sealed class ShadowSettings
{
    /// <summary>Whole-frame budgets, light selection and readiness recovery.</summary>
    [JsonProperty] public ShadowSchedulingSettings Scheduling { get; set; } = new();
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
        Scheduling ??= new();
        Scheduling.Sanitize();
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
    /// <summary>Freezes every owning policy together at the scheduling boundary.</summary>
    internal ShadowSettings Freeze()
    {
        Sanitize();return new(){Scheduling=Scheduling.Freeze(),Inputs=Inputs.Freeze(),Candidates=Candidates.Freeze(),
            Pages=Pages.Freeze(),Drawing=Drawing.Freeze(),Temporal=Temporal.Freeze()};
    }
    #endregion
}
