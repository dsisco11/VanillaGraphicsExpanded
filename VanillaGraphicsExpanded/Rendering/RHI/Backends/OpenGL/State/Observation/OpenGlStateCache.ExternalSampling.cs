using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Propagates established external sampling effects into the existing executed-state authority.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Withholds one mask word during exact dispatch, preserving independent words.</summary>
    internal void WithdrawExternalSampleMask(int index)
    {
        sampleMasks.Remove(index);
    }

    /// <summary>Commits the defined SampleCoverage effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalSampleCoverage(float value, bool invert)
    {
        sampling.SampleCoverage = (Math.Clamp(value, 0, 1), invert); samplingKnown |= SamplingStateKnowledge.SampleCoverage;
    }

    /// <summary>Commits the defined MinSampleShading effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalMinSampleShading(float value)
    {
        sampling.MinimumSampleShading = Math.Clamp(value, 0, 1); samplingKnown |= SamplingStateKnowledge.MinimumSampleShading;
    }

    /// <summary>Commits the defined SampleMask effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalSampleMask(int index, int mask)
    {
        sampleMasks[unchecked((int)index)] = unchecked((uint)mask);
    }

    /// <summary>Commits the defined SampleMask effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalSampleMask(uint index, uint mask)
    {
        sampleMasks[unchecked((int)index)] = unchecked((uint)mask);
    }
    #endregion
}
