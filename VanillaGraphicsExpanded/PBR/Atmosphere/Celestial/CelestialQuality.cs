using System;

namespace VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;

/// <summary>Maps the existing global sky quality selector to bounded celestial work.</summary>
internal readonly record struct CelestialQuality(int Level, int FaceSize, int StarCount, int Octaves)
{
    #region Public API
    /// <summary>Returns the exact admitted budget without silently reducing requested quality.</summary>
    internal static CelestialQuality FromLevel(int level) => level switch
    {
        0 => new(0, 512, 1024, 4), 1 => new(1, 1536, 2048, 5),
        2 => new(2, 2048, 3072, 6), 3 => new(3, 2048, 4096, 7),
        _ => throw new ArgumentOutOfRangeException(nameof(level))
    };
    #endregion
}
