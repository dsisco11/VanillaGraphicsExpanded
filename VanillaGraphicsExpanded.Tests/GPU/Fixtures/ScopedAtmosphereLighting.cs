using System.Numerics;
using System.Reflection;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR.Atmosphere;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Temporarily publishes explicit physical atmosphere sources for isolated transport tests.</summary>
internal sealed class ScopedAtmosphereLighting : IDisposable
{
    private static readonly PropertyInfo Lighting = typeof(AtmosphereModSystem).GetProperty("Lighting",
        BindingFlags.Static | BindingFlags.NonPublic)!;
    private readonly object? previous;

    #region Public API
    /// <summary>Replaces the completed source snapshot with authored solar lux and environment E/pi.</summary>
    public ScopedAtmosphereLighting(Vector3 environment, Vector3 solar = default)
    {
        previous = Lighting.GetValue(null);
        Lighting.SetValue(null, new AtmosphereLighting(Vector3.UnitY, solar, environment,
            Vector3.Zero, Vector3.Zero, []));
    }

    /// <summary>Restores the previous completed publication after the controlled test ends.</summary>
    public void Dispose() => Lighting.SetValue(null, previous);
    #endregion
}
