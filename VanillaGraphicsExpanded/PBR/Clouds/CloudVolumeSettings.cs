using System;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Bounds work and the single additional scattering octave for reference cloud transport.</summary>
internal readonly record struct CloudVolumeSettings(int ViewSamples = 4, int LightSamples = 1,
    float DetailAmplitude = .15f, float EdgeWidth = 2, float NoiseScale = .08f,
    bool Detail = true, float OctaveContribution = .5f, float OctaveOcclusion = .5f)
{
    #region Public API
    /// <summary>Constructs explicit defaults rather than the zero-initialized value-type representation.</summary>
    internal static CloudVolumeSettings Default => new(4, 1, .15f, 2, .08f, true, .5f, .5f);
    /// <summary>Rejects unsupported arithmetic and excessive work before lighting admission or submission.</summary>
    internal void Validate()
    {
        if (ViewSamples is < 1 or > 16 || LightSamples is < 1 or > 4
            || !float.IsFinite(DetailAmplitude) || DetailAmplitude is < 0 or >= 1
            || !float.IsFinite(EdgeWidth) || EdgeWidth is <= 0 or > 10
            || !float.IsFinite(NoiseScale) || NoiseScale is <= 0 or > 1
            || !float.IsFinite(OctaveContribution) || OctaveContribution is < 0 or > 1
            || !float.IsFinite(OctaveOcclusion) || OctaveOcclusion is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(CloudVolumeSettings));
    }
    #endregion
}
