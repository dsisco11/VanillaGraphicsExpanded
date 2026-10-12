using System;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>Preserves the originating transient scale and physical bounds alongside existing history validity.</summary>
internal readonly record struct FrameLightingHistory(uint Calibration, float Scale, FrameLightingBounds Bounds)
{
    #region Public API
    /// <summary>Admits metadata before committing a transient history; no history owner may substitute receiver-local bounds.</summary>
    internal void Validate()
    {
        if (Calibration == 0 || !float.IsFinite(Scale) || Scale < LightingStorage.MinimumFrameScale)
            throw new ArgumentOutOfRangeException(nameof(Scale));
        float bound = Bounds.Total;
        if ((double)Scale * bound > LightingStorage.FrameBudget)
            throw new ArgumentOutOfRangeException(nameof(Bounds));
    }
    /// <summary>Includes this history in the new budget only when its calibration is compatible.</summary>
    internal FrameLightingBounds IncludeIn(FrameLightingBounds current, uint calibration)
    {
        Validate();
        if (Calibration != calibration) throw new InvalidOperationException("Lighting history calibration is incompatible.");
        return current.IncludeHistory(Bounds);
    }
    #endregion
}
