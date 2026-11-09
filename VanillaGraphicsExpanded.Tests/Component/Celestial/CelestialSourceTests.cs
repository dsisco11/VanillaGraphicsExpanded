using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;

namespace VanillaGraphicsExpanded.Tests.Component.Celestial;

/// <summary>Checks full catalog calibration and quality independent lighting data.</summary>
public sealed class CelestialSourceTests
{
    #region Public API
    /// <summary>Checks catalog determinism, normalized color, finite source bounds and quality-independent lighting data.</summary>
    [Fact]
    public void CompleteSourceIsCalibratedAndQualityInvariant()
    {
        // Arrange: select the lowest and highest display quality inputs.
        // Act: build both complete lighting catalogs and integrate uniformly rotated source power.
        var low = CelestialSource.Create(CelestialQuality.FromLevel(0));
        var high = CelestialSource.Create(CelestialQuality.FromLevel(3));
        Vector3 totalStars = low.Stars.Aggregate(Vector3.Zero, (sum, s) => sum + s.Color * s.FluxLux);
        Vector3 totalGalaxy = low.Quadrature.Aggregate(Vector3.Zero, (sum, s) => sum + s.Radiance * s.SolidAngle);
        Vector3 response = Vector3.Zero;
        for (int i = 0; i < 4096; i++)
        {
            float y = (i + .5f) / 4096;
            response += AtmosphereModel.ExternalTransmittance(new(MathF.Sqrt(1 - y * y), y, 0), .001f, 1) * (y / 8192);
        }
        // Assert: display quality does not alter the retained lighting source or its catalog invariants.
        Assert.Equal(low.Stars.ToArray(), high.Stars.ToArray());
        Assert.Equal(low.Quadrature.ToArray(), high.Quadrature.ToArray());
        Assert.Equal(4096, low.Stars.Length);
        Assert.Equal(4096, low.Stars.Select(s => s.Id).Distinct().Count());
        Assert.True(low.MaximumNits * low.StorageScale <= 32000);
        foreach (var star in low.Stars)
        {
            Assert.InRange(star.Direction.Length(), .999999f, 1.000001f);
            Assert.InRange(Vector3.Dot(star.Color, CelestialProceduralField.Luma), .999999f, 1.000001f);
            Assert.True(star.FluxLux > 0 && float.IsFinite(star.FluxLux));
            Assert.True(star.Color.X <= CelestialProceduralField.PaletteMaximum.X && star.Color.Y <= CelestialProceduralField.PaletteMaximum.Y && star.Color.Z <= CelestialProceduralField.PaletteMaximum.Z);
        }
        // Assert: independently integrated star and galaxy power retain their calibrated bounds.
        Assert.InRange(Vector3.Dot(totalStars * response, CelestialProceduralField.Luma), .000099f, .000101f);
        Assert.InRange(Vector3.Dot(totalGalaxy * response, CelestialProceduralField.Luma), .000297f, .000303f);
    }
    #endregion
}
