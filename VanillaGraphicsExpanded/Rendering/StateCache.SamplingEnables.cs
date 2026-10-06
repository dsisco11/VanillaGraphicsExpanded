using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns cached sampling enable transitions.</summary>
internal sealed partial class StateCache
{
    #region Public API
    #region Capability transitions
    /// <summary>Establishes Multisample, preserving independent category knowledge and optional diagnostics.</summary>
    internal void SetMultisampleEnabled(bool enabled)
    {
        ValidateCompleteMutation();
        if (samplingKnown.HasFlag(SamplingStateKnowledge.Multisample) && sampling.Multisample == enabled) return;
        // Withhold knowledge until the native operation and any enabled diagnostics succeed.
        samplingKnown &= ~SamplingStateKnowledge.Multisample;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (enabled) GL.Enable(EnableCap.Multisample); else GL.Disable(EnableCap.Multisample);
        FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        sampling.Multisample = enabled;
        samplingKnown |= SamplingStateKnowledge.Multisample;
    }

    /// <summary>Establishes SampleCoverage, preserving independent category knowledge and optional diagnostics.</summary>
    internal void SetSampleCoverageEnabled(bool enabled)
    {
        ValidateCompleteMutation();
        if (samplingKnown.HasFlag(SamplingStateKnowledge.SampleCoverageEnabled) && sampling.SampleCoverageEnabled == enabled) return;
        // Withhold knowledge until the native operation and any enabled diagnostics succeed.
        samplingKnown &= ~SamplingStateKnowledge.SampleCoverageEnabled;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (enabled) GL.Enable(EnableCap.SampleCoverage); else GL.Disable(EnableCap.SampleCoverage);
        FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        sampling.SampleCoverageEnabled = enabled;
        samplingKnown |= SamplingStateKnowledge.SampleCoverageEnabled;
    }

    /// <summary>Establishes SampleMask, preserving independent category knowledge and optional diagnostics.</summary>
    internal void SetSampleMaskEnabled(bool enabled)
    {
        ValidateCompleteMutation();
        if (samplingKnown.HasFlag(SamplingStateKnowledge.SampleMask) && sampling.SampleMask == enabled) return;
        // Withhold knowledge until the native operation and any enabled diagnostics succeed.
        samplingKnown &= ~SamplingStateKnowledge.SampleMask;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (enabled) GL.Enable(EnableCap.SampleMask); else GL.Disable(EnableCap.SampleMask);
        FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        sampling.SampleMask = enabled;
        samplingKnown |= SamplingStateKnowledge.SampleMask;
    }

    /// <summary>Establishes SampleAlphaToCoverage, preserving independent category knowledge and optional diagnostics.</summary>
    internal void SetSampleAlphaToCoverageEnabled(bool enabled)
    {
        ValidateCompleteMutation();
        if (samplingKnown.HasFlag(SamplingStateKnowledge.SampleAlphaToCoverage) && sampling.SampleAlphaToCoverage == enabled) return;
        // Withhold knowledge until the native operation and any enabled diagnostics succeed.
        samplingKnown &= ~SamplingStateKnowledge.SampleAlphaToCoverage;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (enabled) GL.Enable(EnableCap.SampleAlphaToCoverage); else GL.Disable(EnableCap.SampleAlphaToCoverage);
        FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        sampling.SampleAlphaToCoverage = enabled;
        samplingKnown |= SamplingStateKnowledge.SampleAlphaToCoverage;
    }

    /// <summary>Establishes SampleAlphaToOne, preserving independent category knowledge and optional diagnostics.</summary>
    internal void SetSampleAlphaToOneEnabled(bool enabled)
    {
        ValidateCompleteMutation();
        if (samplingKnown.HasFlag(SamplingStateKnowledge.SampleAlphaToOne) && sampling.SampleAlphaToOne == enabled) return;
        // Withhold knowledge until the native operation and any enabled diagnostics succeed.
        samplingKnown &= ~SamplingStateKnowledge.SampleAlphaToOne;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (enabled) GL.Enable(EnableCap.SampleAlphaToOne); else GL.Disable(EnableCap.SampleAlphaToOne);
        FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        sampling.SampleAlphaToOne = enabled;
        samplingKnown |= SamplingStateKnowledge.SampleAlphaToOne;
    }

    /// <summary>Establishes SampleShading, preserving independent category knowledge and optional diagnostics.</summary>
    internal void SetSampleShadingEnabled(bool enabled)
    {
        ValidateCompleteMutation();
        if (!GpuSupport.Graphics.SampleShading)
        {
            if (enabled) throw new NotSupportedException("SampleShading is unavailable.");
            return;
        }
        if (samplingKnown.HasFlag(SamplingStateKnowledge.SampleShading) && sampling.SampleShading == enabled) return;
        // Withhold knowledge until the native operation and any enabled diagnostics succeed.
        samplingKnown &= ~SamplingStateKnowledge.SampleShading;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (enabled) GL.Enable(EnableCap.SampleShading); else GL.Disable(EnableCap.SampleShading);
        FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        sampling.SampleShading = enabled;
        samplingKnown |= SamplingStateKnowledge.SampleShading;
    }
    #endregion
    #endregion
}
