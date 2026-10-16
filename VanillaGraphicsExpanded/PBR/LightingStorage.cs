using System;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>Defines admitted physical-light storage and outward metadata rounding, independent of camera authoring.</summary>
internal static class LightingStorage
{
    #region Public API
    /// <summary>Fixed multiplier for persistent lux, nits and signed SH coefficients.</summary>
    internal const float PersistentScale = 1f / 16;
    /// <summary>Largest admitted physical scalar before binary16 persistent encoding.</summary>
    internal const float PersistentMaximum = 1048064;
    /// <summary>Largest nonnegative directional nits entering the qualified SH9 projection.</summary>
    internal const float DirectionalMaximum = 131072;
    /// <summary>Minimum frame scale preserving the nominated packed-storage torch fixture.</summary>
    internal const float MinimumFrameScale = 7.147944e-6f;
    /// <summary>Reference stored-light budget for diagnostics and explicit fixtures; not a universal scene-peak guarantee.</summary>
    internal const float FrameBudget = 16000;
    /// <summary>Rounds a nonnegative physical bound outward for publication as Float32 metadata.</summary>
    internal static float UpperBound(double value)
    {
        if (!double.IsFinite(value) || value < 0 || value > float.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(value));
        float rounded = (float)value;
        return rounded < value ? MathF.BitIncrement(rounded) : rounded;
    }
    /// <summary>Selects camera-driven storage while retaining the nominated faint-light floor; scene writes contain outliers.</summary>
    internal static float FrameScale(float cameraMultiplier, float physicalBound)
    {
        if (!float.IsFinite(cameraMultiplier) || cameraMultiplier <= 0
            || !float.IsFinite(physicalBound) || physicalBound < 0)
            throw new ArgumentOutOfRangeException(nameof(physicalBound));
        // A hypothetical narrow specular peak must not reduce precision across every other pixel.
        // Bounds remain source-coherence metadata; destination clamps own exceptional scene values.
        float scale = Math.Max(cameraMultiplier, MinimumFrameScale);
        if (!float.IsFinite(1 / scale) || !float.IsFinite(scale / PersistentScale))
            throw new ArgumentOutOfRangeException(nameof(cameraMultiplier), "The camera scale exceeds supported storage arithmetic.");
        return scale;
    }
    /// <summary>Checks fixed-scale publication before encoding; signed SH coefficients keep their signs.</summary>
    internal static float EncodePersistent(float physical, bool signed = false)
    {
        if (!float.IsFinite(physical) || Math.Abs(physical) > PersistentMaximum || (!signed && physical < 0))
            throw new ArgumentOutOfRangeException(nameof(physical));
        return physical * PersistentScale;
    }
    /// <summary>Decodes an admitted persistent scalar while retaining signed SH coefficient semantics.</summary>
    internal static float DecodePersistent(float stored, bool signed = false)
    {
        if (!float.IsFinite(stored) || Math.Abs(stored) > 65504 || (!signed && stored < 0))
            throw new ArgumentOutOfRangeException(nameof(stored));
        return stored / PersistentScale;
    }
    /// <summary>Validates a temporal handoff and its intermediate against the consumer's supported storage range.</summary>
    internal static float RescaleHistory(float stored, float currentScale, float historyScale, float maximumStored)
    {
        if (!float.IsFinite(stored) || !float.IsFinite(currentScale) || currentScale <= 0
            || !float.IsFinite(historyScale) || historyScale <= 0 || !float.IsFinite(maximumStored) || maximumStored <= 0)
            throw new ArgumentOutOfRangeException(nameof(historyScale));
        float ratio = currentScale / historyScale;
        if (!float.IsFinite(ratio) || ratio <= 0) throw new ArgumentOutOfRangeException(nameof(historyScale));
        double value = (double)stored * ratio;
        if (!double.IsFinite(value) || Math.Abs(value) > maximumStored)
            throw new ArgumentOutOfRangeException(nameof(stored));
        return (float)value;
    }
    #endregion
}
