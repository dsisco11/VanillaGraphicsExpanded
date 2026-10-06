namespace VanillaGraphicsExpanded.Rendering.Pipeline.State;
/// <summary>Concrete sampling values independent of cache knowledge and native operations.</summary>
internal struct SamplingState
{
    /// <summary>Observed SampleCoverage value, valid only when corresponding knowledge is established.</summary>
    public (float Value, bool Invert) SampleCoverage;
    /// <summary>Declared or observed float MinimumSampleShading value.</summary>
    public float MinimumSampleShading;
    // Keep the small flag payload after the parameter values to avoid leading alignment padding.
    private SamplingStateKnowledge booleanValues;

    #region Public API
    #region Boolean values
    /// <summary>Cached native Multisample enable value.</summary>
    public bool Multisample
    {
        readonly get => booleanValues.HasFlag(SamplingStateKnowledge.Multisample);
        set
        {
            if (value) booleanValues |= SamplingStateKnowledge.Multisample;
            else booleanValues &= ~SamplingStateKnowledge.Multisample;
        }
    }

    /// <summary>Cached native SampleCoverageEnabled enable value.</summary>
    public bool SampleCoverageEnabled
    {
        readonly get => booleanValues.HasFlag(SamplingStateKnowledge.SampleCoverageEnabled);
        set
        {
            if (value) booleanValues |= SamplingStateKnowledge.SampleCoverageEnabled;
            else booleanValues &= ~SamplingStateKnowledge.SampleCoverageEnabled;
        }
    }

    /// <summary>Cached native SampleMask enable value.</summary>
    public bool SampleMask
    {
        readonly get => booleanValues.HasFlag(SamplingStateKnowledge.SampleMask);
        set
        {
            if (value) booleanValues |= SamplingStateKnowledge.SampleMask;
            else booleanValues &= ~SamplingStateKnowledge.SampleMask;
        }
    }

    /// <summary>Cached native SampleAlphaToCoverage enable value.</summary>
    public bool SampleAlphaToCoverage
    {
        readonly get => booleanValues.HasFlag(SamplingStateKnowledge.SampleAlphaToCoverage);
        set
        {
            if (value) booleanValues |= SamplingStateKnowledge.SampleAlphaToCoverage;
            else booleanValues &= ~SamplingStateKnowledge.SampleAlphaToCoverage;
        }
    }

    /// <summary>Cached native SampleAlphaToOne enable value.</summary>
    public bool SampleAlphaToOne
    {
        readonly get => booleanValues.HasFlag(SamplingStateKnowledge.SampleAlphaToOne);
        set
        {
            if (value) booleanValues |= SamplingStateKnowledge.SampleAlphaToOne;
            else booleanValues &= ~SamplingStateKnowledge.SampleAlphaToOne;
        }
    }

    /// <summary>Cached native SampleShading enable value.</summary>
    public bool SampleShading
    {
        readonly get => booleanValues.HasFlag(SamplingStateKnowledge.SampleShading);
        set
        {
            if (value) booleanValues |= SamplingStateKnowledge.SampleShading;
            else booleanValues &= ~SamplingStateKnowledge.SampleShading;
        }
    }
    #endregion
    #endregion
}
