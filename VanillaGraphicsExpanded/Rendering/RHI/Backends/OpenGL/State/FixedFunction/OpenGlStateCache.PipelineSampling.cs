using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Establishes conservative sampling enables and only their enabled parameter payloads.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Private
    /// <summary>Keeps inactive coverage values, mask words and minimum shading untouched.</summary>
    private void ApplyPipelineSampling(SamplingDesc value, GraphicsStateRelevance relevance)
    {
        var groups = relevance.Sampling;
        if (groups.HasFlag(SamplingStateKnowledge.Multisample)) SetMultisampleEnabled(value.Multisample);
        if (groups.HasFlag(SamplingStateKnowledge.SampleCoverageEnabled)) SetSampleCoverageEnabled(value.CoverageEnabled);
        if (groups.HasFlag(SamplingStateKnowledge.SampleCoverage)) SetSampleCoverage(value.Coverage, value.CoverageInvert);
        if (groups.HasFlag(SamplingStateKnowledge.SampleMask)) SetSampleMaskEnabled(value.MaskEnabled);
        if (relevance.SampleMaskWords)
            for (int word = 0; word < GpuSupport.Graphics.MaxSampleMaskWords; word++) SetSampleMask(word, value.GetMaskWord(word));
        if (groups.HasFlag(SamplingStateKnowledge.SampleAlphaToCoverage)) SetSampleAlphaToCoverageEnabled(value.AlphaToCoverage);
        if (groups.HasFlag(SamplingStateKnowledge.SampleAlphaToOne)) SetSampleAlphaToOneEnabled(value.AlphaToOne);
        if (groups.HasFlag(SamplingStateKnowledge.SampleShading)) SetSampleShadingEnabled(value.SampleShading);
        if (groups.HasFlag(SamplingStateKnowledge.MinimumSampleShading)) SetMinimumSampleShading(value.MinimumSampleShading);
    }
    #endregion
}
