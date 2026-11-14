using System.Collections.Immutable;
using System.Numerics;
using VanillaGraphicsExpanded.PBR.Shadows;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;

namespace VanillaGraphicsExpanded.Tests.Support.Lighting;

/// <summary>Builds explicit surface light snapshots for independent shader views with shadows unavailable.</summary>
internal static class SurfaceShadowTestInputs
{
    private static readonly ImmutableArray<double> Identity =
        [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

    #region Public API
    /// <summary>Creates independent current celestial terms without inventing a shadow publication.</summary>
    internal static SurfaceShadowInputSet Create(Vector3 sunDirection, Vector3 solar,
        Vector3? moonDirection = null, Vector3? lunar = null)
    {
        var sun = new ShadowDirectionalLight(new(7, 0, ShadowSourceKind.Sun, 1, 1, 0),
            Vector3.Normalize(sunDirection), solar);
        var moon = new ShadowDirectionalLight(new(7, 0, ShadowSourceKind.Moon, 2, 2, 0),
            Vector3.Normalize(moonDirection ?? Vector3.UnitY), lunar ?? Vector3.Zero);
        var lights = new ShadowLightSnapshot(1, 7, 0, [], sun, moon, Identity,
            0, 0, 0, Vector3.Zero);
        return new SurfaceShadowInputSet(lights, null, null, new ShadowPageSettings());
    }
    #endregion
}
