using System;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

/// <summary>Immutable sampling controls; omitted mask words explicitly mean all ones, never inherited state.</summary>
internal sealed record SamplingDesc
{
    public bool Multisample { get; init; } = true;
    public bool CoverageEnabled { get; init; }
    public float Coverage { get; init; } = 1;
    public bool CoverageInvert { get; init; }
    public bool MaskEnabled { get; init; }
    public PipelineValues<uint>? Masks { get; init; }
    public bool AlphaToCoverage { get; init; }
    public bool AlphaToOne { get; init; }
    public bool SampleShading { get; init; }
    public float MinimumSampleShading { get; init; }

    #region Public API
    /// <summary>Resolves every supported native mask word, including the explicit all-ones suffix.</summary>
    public uint GetMaskWord(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return MaskEnabled && Masks is { } masks && index < masks.Count ? masks[index] : uint.MaxValue;
    }
    #endregion
}
