using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns cached sampling transitions.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Establishes SampleCoverage while suppressing a known identical native transition.</summary>
    internal void SetSampleCoverage(float value, bool invert)
    {
        if (!float.IsFinite(value) || value < 0 || value > 1) throw new ArgumentOutOfRangeException(nameof(value));
        ValidateBoundaryMutation(sampling: SamplingStateKnowledge.SampleCoverage, completeGraphics: true);
        if (samplingKnown.HasFlag(SamplingStateKnowledge.SampleCoverage) && sampling.SampleCoverage == (value, invert)) return;
        samplingKnown &= ~SamplingStateKnowledge.SampleCoverage;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        OpenGLStateEffects.SampleCoverage(value, invert);
#if DEBUG
        FixedFunctionCalls++;
#endif
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        sampling.SampleCoverage = (value, invert);
        samplingKnown |= SamplingStateKnowledge.SampleCoverage;
    }

    /// <summary>Establishes MinimumSampleShading while suppressing a known identical native transition.</summary>
    internal void SetMinimumSampleShading(float value)
    {
        if (!float.IsFinite(value) || value < 0 || value > 1) throw new ArgumentOutOfRangeException(nameof(value));
        ValidateBoundaryMutation(sampling: SamplingStateKnowledge.MinimumSampleShading, completeGraphics: true);
        if (samplingKnown.HasFlag(SamplingStateKnowledge.MinimumSampleShading) && sampling.MinimumSampleShading == value) return;
        samplingKnown &= ~SamplingStateKnowledge.MinimumSampleShading;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        OpenGLStateEffects.MinSampleShading(value);
#if DEBUG
        FixedFunctionCalls++;
#endif
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        sampling.MinimumSampleShading = value;
        samplingKnown |= SamplingStateKnowledge.MinimumSampleShading;
    }

    /// <summary>Establishes one independently tracked native sample-mask word.</summary>
    internal void SetSampleMask(int index, uint value)
    {
        if (index < 0 || index >= GpuSupport.Graphics.MaxSampleMaskWords) throw new ArgumentOutOfRangeException(nameof(index));
        ValidateBoundaryMutation(sampleMaskWords: true, completeGraphics: true);
        if (sampleMasks.TryGetValue(index, out uint known) && known == value) return;
        sampleMasks.Remove(index);
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        OpenGLStateEffects.SampleMask((uint)index, value);
#if DEBUG
        FixedFunctionCalls++;
#endif
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        sampleMasks[index] = value;
    }
    #endregion
}
