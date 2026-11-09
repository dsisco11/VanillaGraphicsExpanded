using VanillaGraphicsExpanded.PBR.Atmosphere;

namespace VanillaGraphicsExpanded.Tests.Component.Support.Lighting;

/// <summary>Provides independent scalar references for finite-source solar BRDF checks.</summary>
internal static class FiniteSolarBrdfReference
{
    #region Public API
    /// <summary>Derives the normal-incidence finite-solar response independently in double precision.</summary>
    internal static float NormalIncidenceExpected(float f0, float roughness)
    {
        double r = Math.Clamp(roughness, .04, 1);
        double alpha = r * r;
        double alphaSquared = alpha * alpha;
        double sine = Math.Sin(AtmosphereSolarDisk.AngularRadius) * (1 - r * r);
        double extended = alphaSquared + .25 * sine * (3 * alpha + sine) / 1.001;
        return (float)(f0 / (4 * Math.PI * extended));
    }
    #endregion
}
