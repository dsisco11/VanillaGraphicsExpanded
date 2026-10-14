using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Qualifies the deterministic source, bounded colors and atmospheric-reference energy partition.</summary>
public sealed class CelestialSourceTests
{
    #region Public API
    /// <summary>Locks unsigned hash wrapping independently of GPU/compiler implementations.</summary>
    [Fact]
    public void HashFixturesAndQualityBudgetsAreStable()
    {
        Assert.Equal(0u, CelestialProceduralField.Hash(0));
        Assert.Equal(0x688990c0u, CelestialProceduralField.Hash(1));
        Assert.Equal(new[] { 512, 1536, 2048, 2048 }, Enumerable.Range(0, 4).Select(i => CelestialQuality.FromLevel(i).FaceSize));
        Assert.Equal(new[] { 1024, 2048, 3072, 4096 }, Enumerable.Range(0, 4).Select(i => CelestialQuality.FromLevel(i).StarCount));
        Assert.Throws<ArgumentOutOfRangeException>(() => CelestialQuality.FromLevel(4));
    }

    /// <summary>Checks catalog determinism, normalized color, finite source bounds and quality-independent lighting data.</summary>
    [Fact]
    public void CompleteSourceIsCalibratedAndQualityInvariant()
    {
        var low = CelestialSource.Create(CelestialQuality.FromLevel(0));
        var high = CelestialSource.Create(CelestialQuality.FromLevel(3));
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
        // Independently integrate many elevation samples of uniformly rotated source power.
        Vector3 totalStars = low.Stars.Aggregate(Vector3.Zero, (sum, s) => sum + s.Color * s.FluxLux);
        Vector3 totalGalaxy = low.Quadrature.Aggregate(Vector3.Zero, (sum, s) => sum + s.Radiance * s.SolidAngle);
        Vector3 response = Vector3.Zero;
        for (int i = 0; i < 4096; i++)
        {
            float y = (i + .5f) / 4096;
            response += AtmosphereModel.ExternalTransmittance(new(MathF.Sqrt(1 - y * y), y, 0), .001f, 1) * (y / 8192);
        }
        Assert.InRange(Vector3.Dot(totalStars * response, CelestialProceduralField.Luma), .000099f, .000101f);
        Assert.InRange(Vector3.Dot(totalGalaxy * response, CelestialProceduralField.Luma), .000297f, .000303f);
    }

    /// <summary>The exact cube Jacobian covers the sphere even at odd and non-power-of-two dimensions.</summary>
    [Theory]
    [InlineData(3)] [InlineData(64)] [InlineData(1536)]
    public void SolidAngleCoversSphere(int size)
    {
        double area = 0;
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) area += 6 * CelestialCubeGeometry.SolidAngle(x, y, size);
        Assert.InRange(Math.Abs(area - 4 * Math.PI), 0, 1e-5);
    }

    /// <summary>Checks the cross-face cap conservatively includes every positive support sample around an edge.</summary>
    [Fact]
    public void ResidualBoundsIncludeFaceEdges()
    {
        Vector3 star = Vector3.Normalize(new Vector3(1, .1f, 1));
        const int size = 128;
        int supportedFaces = 0;
        for (int face = 0; face < 6; face++)
        {
            var bounds = CelestialResidual.Bounds(star, face, size); bool found = false;
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                Vector3 d = CelestialCubeGeometry.Direction(face, 2f * (x + .5f) / size - 1, 2f * (y + .5f) / size - 1);
                if (CelestialResidual.Kernel(d, star) <= 0) continue;
                found = true; Assert.InRange(x, bounds.X0, bounds.X1); Assert.InRange(y, bounds.Y0, bounds.Y1);
            }
            if (found) supportedFaces++;
        }
        Assert.Equal(2, supportedFaces);
    }

    /// <summary>Checks continuous shared-face coordinates and qualifies diffuse quadrature against a refined directional grid.</summary>
    [Fact]
    public void GalaxyIsContinuousAndQuadratureConverges()
    {
        for (int i = 0; i <= 32; i++)
        {
            float v = 2f * i / 32 - 1;
            var right = CelestialCubeGeometry.Direction(0, 1, v);
            var neighbor = CelestialCubeGeometry.Direction(5, -1, v);
            Assert.InRange(Vector3.Distance(right, neighbor), 0, 1e-6f);
            Assert.Equal(CelestialProceduralField.Evaluate(right, 7, .01f), CelestialProceduralField.Evaluate(neighbor, 7, .01f));
        }
        var source = CelestialSource.Create(CelestialQuality.FromLevel(3));
        Vector3 coarse = source.Quadrature.Aggregate(Vector3.Zero, (sum, s) => sum + s.Radiance * s.SolidAngle);
        Vector3 refined = Vector3.Zero;
        const int size = 128;
        for (int face = 0; face < 6; face++) for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            var direction = CelestialCubeGeometry.Direction(face, 2f * (x + .5f) / size - 1, 2f * (y + .5f) / size - 1);
            refined += CelestialProceduralField.Evaluate(direction, 7, 2f / size) * source.GalaxyAmplitude * CelestialCubeGeometry.SolidAngle(x, y, size);
        }
        Assert.InRange(MathF.Abs(refined.X / coarse.X - 1), 0, .01f);
        Assert.InRange(MathF.Abs(refined.Y / coarse.Y - 1), 0, .01f);
        Assert.InRange(MathF.Abs(refined.Z / coarse.Z - 1), 0, .01f);
    }
    #endregion
}
