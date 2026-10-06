namespace VanillaGraphicsExpanded.Rendering.Pipeline.State;
/// <summary>Concrete sampling values independent of cache knowledge and native operations.</summary>
internal struct SamplingState
{
    /// <summary>Observed SampleCoverage value, valid only when corresponding knowledge is established.</summary>
    public (float Value, bool Invert) SampleCoverage;
    /// <summary>Declared or observed float MinimumSampleShading value.</summary>
    public float MinimumSampleShading;
    /// <summary>Cached native Multisample enable value.</summary>
    public bool Multisample;
    /// <summary>Cached native SampleCoverageEnabled enable value.</summary>
    public bool SampleCoverageEnabled;
    /// <summary>Cached native SampleMask enable value.</summary>
    public bool SampleMask;
    /// <summary>Cached native SampleAlphaToCoverage enable value.</summary>
    public bool SampleAlphaToCoverage;
    /// <summary>Cached native SampleAlphaToOne enable value.</summary>
    public bool SampleAlphaToOne;
    /// <summary>Cached native SampleShading enable value.</summary>
    public bool SampleShading;
}
