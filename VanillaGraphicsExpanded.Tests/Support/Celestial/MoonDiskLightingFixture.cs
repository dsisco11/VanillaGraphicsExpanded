using System.Collections.Immutable;
using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere;

namespace VanillaGraphicsExpanded.Tests.Support.Celestial;

/// <summary>Creates a controlled published lighting snapshot for lunar disk tests.</summary>
internal static class MoonDiskLightingFixture
{
    #region Public API
    /// <summary>Supplies already extinguished colored incident lux and a controlled apparent lunar size.</summary>
    internal static AtmosphereLighting Snapshot(float phase) =>
        new(Vector3.UnitX, Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.One, ImmutableArray<float>.Empty)
        {
            HasNightSources = true, Moon = -Vector3.UnitZ, MoonPhase = phase,
            MoonAngularRadius = .03252f, HorizonElevation = -.1f, Lunar = new(.01f, .008f, .006f)
        };
    #endregion
}
