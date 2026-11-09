using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;

namespace VanillaGraphicsExpanded.Tests.Unit.Celestial;

/// <summary>Checks deterministic source rules, sphere coverage, and directional continuity.</summary>
public sealed class CelestialSourceTests
{
    #region Public API
    /// <summary>Locks unsigned hash wrapping independently of GPU/compiler implementations.</summary>
    [Fact]
    public void HashFixturesAndQualityBudgetsAreStable()
    {
        // Arrange: choose two fixed hash inputs and all published quality levels.
        // Act: evaluate the hashes, budgets, and out-of-range level.
        // Assert: wrapping fixtures are stable and unsupported levels fail.
        Assert.Equal(0u, CelestialProceduralField.Hash(0));
        Assert.Equal(0x688990c0u, CelestialProceduralField.Hash(1));
        Assert.Equal(new[] { 512, 1536, 2048, 2048 }, Enumerable.Range(0, 4).Select(i => CelestialQuality.FromLevel(i).FaceSize));
        Assert.Equal(new[] { 1024, 2048, 3072, 4096 }, Enumerable.Range(0, 4).Select(i => CelestialQuality.FromLevel(i).StarCount));
        Assert.Throws<ArgumentOutOfRangeException>(() => CelestialQuality.FromLevel(4));
    }
    /// <summary>The exact cube Jacobian covers the sphere even at odd and non-power-of-two dimensions.</summary>
    [Theory]
    [InlineData(3)] [InlineData(64)] [InlineData(1536)]
    public void SolidAngleCoversSphere(int size)
    {
        // Arrange: use each requested face dimension, including odd and non-power-of-two sizes.
        double area = 0;
        // Act: sum the cube Jacobian over all six faces.
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) area += 6 * CelestialCubeGeometry.SolidAngle(x, y, size);
        // Assert: the authored area covers one sphere.
        Assert.InRange(Math.Abs(area - 4 * Math.PI), 0, 1e-5);
    }

    /// <summary>Checks the cross-face cap conservatively includes every positive support sample around an edge.</summary>
    [Fact]
    public void ResidualBoundsIncludeFaceEdges()
    {
        // Arrange: choose a star whose residual support crosses a cube-face edge.
        Vector3 star = Vector3.Normalize(new Vector3(1, .1f, 1));
        const int size = 128;
        int supportedFaces = 0;
        for (int face = 0; face < 6; face++)
        {
            // Act: inspect every sample that has positive support on this face.
            var bounds = CelestialResidual.Bounds(star, face, size); bool found = false;
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                Vector3 d = CelestialCubeGeometry.Direction(face, 2f * (x + .5f) / size - 1, 2f * (y + .5f) / size - 1);
                if (CelestialResidual.Kernel(d, star) <= 0) continue;
                found = true;
                // Assert: each supported sample lies inside the conservative face bounds.
                Assert.InRange(x, bounds.X0, bounds.X1); Assert.InRange(y, bounds.Y0, bounds.Y1);
            }
            if (found) supportedFaces++;
        }
        Assert.Equal(2, supportedFaces);
    }
    /// <summary>Checks continuous shared-face coordinates and qualifies diffuse quadrature against a refined directional grid.</summary>
    [Fact]
    public void GalaxyIsContinuousAndQuadratureConverges()
    {
        // Arrange: compare matching coordinates at one shared cube edge.
        // Act/Assert: adjacent faces map the shared edge to identical directions and radiance.
        for (int i = 0; i <= 32; i++)
        {
            float v = 2f * i / 32 - 1;
            var right = CelestialCubeGeometry.Direction(0, 1, v);
            var neighbor = CelestialCubeGeometry.Direction(5, -1, v);
            Assert.InRange(Vector3.Distance(right, neighbor), 0, 1e-6f);
            Assert.Equal(CelestialProceduralField.Evaluate(right, 7, .01f), CelestialProceduralField.Evaluate(neighbor, 7, .01f));
        }
        // Act: create the retained source quadrature and an independent refined grid.
        var source = CelestialSource.Create(CelestialQuality.FromLevel(3));
        Vector3 coarse = source.Quadrature.Aggregate(Vector3.Zero, (sum, s) => sum + s.Radiance * s.SolidAngle);
        Vector3 refined = Vector3.Zero;
        const int size = 128;
        for (int face = 0; face < 6; face++) for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            var direction = CelestialCubeGeometry.Direction(face, 2f * (x + .5f) / size - 1, 2f * (y + .5f) / size - 1);
            refined += CelestialProceduralField.Evaluate(direction, 7, 2f / size) * source.GalaxyAmplitude * CelestialCubeGeometry.SolidAngle(x, y, size);
        }
        // Assert: the two independent grids agree within one percent in every channel.
        Assert.InRange(MathF.Abs(refined.X / coarse.X - 1), 0, .01f);
        Assert.InRange(MathF.Abs(refined.Y / coarse.Y - 1), 0, .01f);
        Assert.InRange(MathF.Abs(refined.Z / coarse.Z - 1), 0, .01f);
    }
    #endregion
}
