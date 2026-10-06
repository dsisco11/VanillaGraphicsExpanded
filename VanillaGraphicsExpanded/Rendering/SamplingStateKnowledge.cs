using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Independent validity flags for cached SamplingState values.</summary>
[Flags]
internal enum SamplingStateKnowledge : byte
{
    /// <summary>No fields are known.</summary>
    None = 0,
    /// <summary>The cached SampleCoverage value is known.</summary>
    SampleCoverage = 1 << 0,
    /// <summary>The cached MinimumSampleShading value is known.</summary>
    MinimumSampleShading = 1 << 1,
    /// <summary>The cached Multisample value is known.</summary>
    Multisample = 1 << 2,
    /// <summary>The cached SampleCoverageEnabled value is known.</summary>
    SampleCoverageEnabled = 1 << 3,
    /// <summary>The cached SampleMask value is known.</summary>
    SampleMask = 1 << 4,
    /// <summary>The cached SampleAlphaToCoverage value is known.</summary>
    SampleAlphaToCoverage = 1 << 5,
    /// <summary>The cached SampleAlphaToOne value is known.</summary>
    SampleAlphaToOne = 1 << 6,
    /// <summary>The cached SampleShading value is known.</summary>
    SampleShading = 1 << 7,
    /// <summary>Every scalar field in this category is known.</summary>
    All = SampleCoverage | MinimumSampleShading | Multisample | SampleCoverageEnabled | SampleMask | SampleAlphaToCoverage | SampleAlphaToOne | SampleShading
}
