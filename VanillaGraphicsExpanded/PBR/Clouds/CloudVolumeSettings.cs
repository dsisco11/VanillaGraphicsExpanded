using System;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Bounds work and the single additional scattering octave for reference cloud transport.</summary>
internal readonly record struct CloudVolumeSettings(int LightRaySampleMax = 10,
    float DetailAmplitude = .15f, float EdgeWidth = 2, float NoiseScale = .08f,
    bool Detail = true, float OctaveContribution = .5f, float OctaveOcclusion = .5f,
    int ViewRaySampleMax = 384, int ViewRaySampleMin = 2, float ViewBudgetDistance = 50, float ShadowDistance = 1500, float StopTransmittance = .005f, CloudTraceScale TraceScale = CloudTraceScale.Half)
{
    #region Public API
    /// <summary>Constructs explicit defaults rather than the zero-initialized value-type representation.</summary>
    internal static CloudVolumeSettings Default => new(10, .15f, 2, .08f, true, .5f, .5f);
    /// <summary>Rejects unsupported arithmetic and excessive work before lighting admission or submission.</summary>
    internal void Validate()
    {
        if (TraceScale is not (CloudTraceScale.Half or CloudTraceScale.Quarter)
            || !float.IsFinite(StopTransmittance) || StopTransmittance is < 0 or > .005f
            || LightRaySampleMax is < 2 or > 64
            || !float.IsFinite(ShadowDistance) || ShadowDistance is < 50 or > 10000
            || ViewRaySampleMax is < 4 or > 384 || ViewRaySampleMin < 1
            || ViewRaySampleMin > Math.Min(16, ViewRaySampleMax)
            || !float.IsFinite(ViewBudgetDistance) || ViewBudgetDistance is < 50 or > 10000
            || !float.IsFinite(DetailAmplitude) || DetailAmplitude is < 0 or >= 1
            || !float.IsFinite(EdgeWidth) || EdgeWidth is <= 0 or > 10
            || !float.IsFinite(NoiseScale) || NoiseScale is <= 0 or > 1
            || !float.IsFinite(OctaveContribution) || OctaveContribution is < 0 or > 1
            || !float.IsFinite(OctaveOcclusion) || OctaveOcclusion is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(CloudVolumeSettings));
    }
    #endregion
}
