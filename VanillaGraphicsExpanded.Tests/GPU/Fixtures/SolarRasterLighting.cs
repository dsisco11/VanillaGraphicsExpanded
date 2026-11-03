using System.Collections.Immutable;
using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Builds physical atmosphere inputs that reproduce a requested finite solar-disk radiance.</summary>
internal static class SolarRasterLighting
{
    #region Public API
    /// <summary>Converts a controlled disk radiance into the irradiance consumed by the production sun shader.</summary>
    internal static AtmosphereLighting Create(Vector3 radiance, float horizonElevation)
    {
        float visibility = AtmosphereSolarDisk.Visibility(0, horizonElevation);
        float projectedArea = visibility * MathF.PI * MathF.Pow(MathF.Sin(AtmosphereSolarDisk.AngularRadius), 2);
        // Invert the production radiance conversion so the real owner reproduces the controlled fixture value.
        return new AtmosphereLighting(-Vector3.UnitZ, radiance * projectedArea,
            Vector3.Zero, Vector3.Zero, Vector3.One, ImmutableArray<float>.Empty)
        {
            HorizonElevation = horizonElevation
        };
    }
    #endregion
}
