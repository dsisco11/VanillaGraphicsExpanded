using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns cached sampling enable transitions.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    #region Capability transitions
    /// <summary>Establishes Multisample, preserving independent category knowledge and optional diagnostics.</summary>
    internal void SetMultisampleEnabled(bool enabled)
    {
        ValidateBoundaryMutation(sampling: SamplingStateKnowledge.Multisample, completeGraphics: true);
        if (samplingKnown.HasFlag(SamplingStateKnowledge.Multisample) && sampling.Multisample == enabled) return;
        // Withhold knowledge until the native operation and any enabled diagnostics succeed.
        samplingKnown &= ~SamplingStateKnowledge.Multisample;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (enabled) OpenGLStateEffects.Enable(EnableCap.Multisample); else OpenGLStateEffects.Disable(EnableCap.Multisample);
#if DEBUG
        FixedFunctionCalls++;
#endif
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        sampling.Multisample = enabled;
        samplingKnown |= SamplingStateKnowledge.Multisample;
    }

    /// <summary>Establishes SampleCoverage, preserving independent category knowledge and optional diagnostics.</summary>
    internal void SetSampleCoverageEnabled(bool enabled)
    {
        ValidateBoundaryMutation(sampling: SamplingStateKnowledge.SampleCoverageEnabled, completeGraphics: true);
        if (samplingKnown.HasFlag(SamplingStateKnowledge.SampleCoverageEnabled) && sampling.SampleCoverageEnabled == enabled) return;
        // Withhold knowledge until the native operation and any enabled diagnostics succeed.
        samplingKnown &= ~SamplingStateKnowledge.SampleCoverageEnabled;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (enabled) OpenGLStateEffects.Enable(EnableCap.SampleCoverage); else OpenGLStateEffects.Disable(EnableCap.SampleCoverage);
#if DEBUG
        FixedFunctionCalls++;
#endif
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        sampling.SampleCoverageEnabled = enabled;
        samplingKnown |= SamplingStateKnowledge.SampleCoverageEnabled;
    }

    /// <summary>Establishes SampleMask, preserving independent category knowledge and optional diagnostics.</summary>
    internal void SetSampleMaskEnabled(bool enabled)
    {
        ValidateBoundaryMutation(sampling: SamplingStateKnowledge.SampleMask, completeGraphics: true);
        if (samplingKnown.HasFlag(SamplingStateKnowledge.SampleMask) && sampling.SampleMask == enabled) return;
        // Withhold knowledge until the native operation and any enabled diagnostics succeed.
        samplingKnown &= ~SamplingStateKnowledge.SampleMask;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (enabled) OpenGLStateEffects.Enable(EnableCap.SampleMask); else OpenGLStateEffects.Disable(EnableCap.SampleMask);
#if DEBUG
        FixedFunctionCalls++;
#endif
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        sampling.SampleMask = enabled;
        samplingKnown |= SamplingStateKnowledge.SampleMask;
    }

    /// <summary>Establishes SampleAlphaToCoverage, preserving independent category knowledge and optional diagnostics.</summary>
    internal void SetSampleAlphaToCoverageEnabled(bool enabled)
    {
        ValidateBoundaryMutation(sampling: SamplingStateKnowledge.SampleAlphaToCoverage, completeGraphics: true);
        if (samplingKnown.HasFlag(SamplingStateKnowledge.SampleAlphaToCoverage) && sampling.SampleAlphaToCoverage == enabled) return;
        // Withhold knowledge until the native operation and any enabled diagnostics succeed.
        samplingKnown &= ~SamplingStateKnowledge.SampleAlphaToCoverage;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (enabled) OpenGLStateEffects.Enable(EnableCap.SampleAlphaToCoverage); else OpenGLStateEffects.Disable(EnableCap.SampleAlphaToCoverage);
#if DEBUG
        FixedFunctionCalls++;
#endif
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        sampling.SampleAlphaToCoverage = enabled;
        samplingKnown |= SamplingStateKnowledge.SampleAlphaToCoverage;
    }

    /// <summary>Establishes SampleAlphaToOne, preserving independent category knowledge and optional diagnostics.</summary>
    internal void SetSampleAlphaToOneEnabled(bool enabled)
    {
        ValidateBoundaryMutation(sampling: SamplingStateKnowledge.SampleAlphaToOne, completeGraphics: true);
        if (samplingKnown.HasFlag(SamplingStateKnowledge.SampleAlphaToOne) && sampling.SampleAlphaToOne == enabled) return;
        // Withhold knowledge until the native operation and any enabled diagnostics succeed.
        samplingKnown &= ~SamplingStateKnowledge.SampleAlphaToOne;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (enabled) OpenGLStateEffects.Enable(EnableCap.SampleAlphaToOne); else OpenGLStateEffects.Disable(EnableCap.SampleAlphaToOne);
#if DEBUG
        FixedFunctionCalls++;
#endif
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        sampling.SampleAlphaToOne = enabled;
        samplingKnown |= SamplingStateKnowledge.SampleAlphaToOne;
    }

    /// <summary>Establishes SampleShading, preserving independent category knowledge and optional diagnostics.</summary>
    internal void SetSampleShadingEnabled(bool enabled)
    {
        ValidateBoundaryMutation(sampling: SamplingStateKnowledge.SampleShading, completeGraphics: true);

        if (samplingKnown.HasFlag(SamplingStateKnowledge.SampleShading) && sampling.SampleShading == enabled) return;
        // Withhold knowledge until the native operation and any enabled diagnostics succeed.
        samplingKnown &= ~SamplingStateKnowledge.SampleShading;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (enabled) OpenGLStateEffects.Enable(EnableCap.SampleShading); else OpenGLStateEffects.Disable(EnableCap.SampleShading);
#if DEBUG
        FixedFunctionCalls++;
#endif
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        sampling.SampleShading = enabled;
        samplingKnown |= SamplingStateKnowledge.SampleShading;
    }
    #endregion
    #endregion
}
