using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Native sampling parameter values and their independently valid fields.</summary>
internal struct CompleteSamplingState
{
    /// <summary>Independent knowledge for observed native values.</summary>
    public CompleteSamplingKnowledge Known;
    /// <summary>Supplemental native enable values and their validity.</summary>
    public CompleteEnableState Enables;
    /// <summary>Observed SampleCoverage value, valid only when corresponding knowledge is established.</summary>
    public (float Value, bool Invert) SampleCoverage;
    /// <summary>Declared or observed float MinimumSampleShading value.</summary>
    public float MinimumSampleShading;
}
