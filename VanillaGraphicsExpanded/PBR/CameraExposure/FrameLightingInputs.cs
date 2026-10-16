using System;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.CameraExposure;

/// <summary>Packs an admitted physical frame bound and camera seed without reading exposure back from the GPU.</summary>
internal sealed class FrameLightingInputs : CpuUniformBuffer
{
    #region Public API
    /// <summary>Allocates two std140 vectors under the existing program-owned input allocator.</summary>
    internal FrameLightingInputs() : base(32) { }
    /// <summary>Validates the entire selection domain before changing the retained publication.</summary>
    internal void Capture(float physicalBound, float manualMultiplier, bool automaticHistory, uint frame, uint calibrationGeneration)
    {
        // Source metadata stays validated without treating its estimated peak as an all-pixel precision constraint.
        if (!float.IsFinite(manualMultiplier) || manualMultiplier <= 0 || calibrationGeneration == 0)
            throw new ArgumentOutOfRangeException(nameof(manualMultiplier));
        _ = LightingStorage.FrameScale(manualMultiplier, physicalBound);
        WriteVector4(0, new(physicalBound, manualMultiplier, automaticHistory ? 1 : 0, 0));
        WriteUInt32(16, frame); WriteUInt32(20, calibrationGeneration);
        WriteUInt32(24, 0); WriteUInt32(28, 0);
    }
    #endregion
}
