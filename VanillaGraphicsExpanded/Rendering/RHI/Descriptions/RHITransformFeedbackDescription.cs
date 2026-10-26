using System;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Supplies frozen output ranges for a transform-feedback object.</summary>
public sealed class RHITransformFeedbackDescription
{
    private readonly RHITransformFeedbackOutput[] outputs;
    #region Public API
    /// <summary>Creates an independent copy of supplied configuration without resource inspection.</summary>
    public RHITransformFeedbackDescription(ReadOnlySpan<RHITransformFeedbackOutput> outputs = default) => this.outputs = outputs.ToArray();
    /// <summary>Exposes immutable authored output selections.</summary>
    public ReadOnlySpan<RHITransformFeedbackOutput> Outputs => outputs;
    #endregion
}
