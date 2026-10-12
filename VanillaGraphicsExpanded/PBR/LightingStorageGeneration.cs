using System;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>Identifies the calibration and encoding under which persistent lighting was committed.</summary>
internal readonly record struct LightingStorageGeneration(uint Calibration, float Scale)
{
    #region Public API
    /// <summary>Creates admitted fixed-scale metadata; zero is reserved for unavailable physical publications.</summary>
    internal static LightingStorageGeneration Persistent(uint calibration)
    {
        if (calibration == 0) throw new ArgumentOutOfRangeException(nameof(calibration));
        return new(calibration, LightingStorage.PersistentScale);
    }
    /// <summary>Rejects a cache generation when calibration or encoding changes, independently of camera exposure.</summary>
    internal bool CompatibleWith(LightingStorageGeneration current) => Calibration != 0
        && Calibration == current.Calibration && Scale == LightingStorage.PersistentScale && Scale == current.Scale;
    #endregion
}
