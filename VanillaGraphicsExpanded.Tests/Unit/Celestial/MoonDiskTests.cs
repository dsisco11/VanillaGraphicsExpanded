using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;
using VanillaGraphicsExpanded.Tests.Support.Celestial;

namespace VanillaGraphicsExpanded.Tests.Unit.Celestial;

/// <summary>Checks lunar disk admission against independently integrated projected Lambert power.</summary>
public sealed class MoonDiskTests
{
    #region Public API
    /// <summary>Every admitted phase preserves completed lunar power without a second phase or extinction multiplier.</summary>
    [Theory]
    [InlineData(1f)] [InlineData(2f)] [InlineData(4f)] [InlineData(6f)] [InlineData(7f)]
    public void SurfaceIntegratesToPublishedLunarPower(float phase)
    {
        // Arrange: create a completed colored lunar publication for the selected phase.
        var lighting = MoonDiskLightingFixture.Snapshot(phase);
        // Act: capture the disk and independently integrate its projected surface.
        var disk = MoonDisk.Capture(lighting);
        double integral = Integrate(disk);
        // Assert: the normalized surface reproduces admitted channel power and a valid basis.
        for (int channel = 0; channel < 3; channel++)
            Assert.InRange(Math.Abs(disk.Radiance[channel] * integral / lighting.Lunar[channel] - 1), 0, .01);
        Assert.True(disk.MaximumNits >= Math.Max(disk.Radiance.X, Math.Max(disk.Radiance.Y, disk.Radiance.Z)));
        Assert.InRange(Vector3.Dot(disk.Right, disk.Direction), -.000001f, .000001f);
        Assert.InRange(Vector3.Dot(disk.Up, disk.Direction), -.000001f, .000001f);
    }

    /// <summary>Missing, new-moon and horizon-hidden sources remain dark, while invalid nonzero hidden power is rejected.</summary>
    [Fact]
    public void DarkAndInvalidPublicationsCannotInventLight()
    {
        // Arrange: start from an admitted lunar publication.
        var lighting = MoonDiskLightingFixture.Snapshot(4);
        // Act: capture disks for missing, new-moon, hidden, and malformed hidden publications.
        // Assert: dark sources stay dark and invalid nonzero hidden power is rejected.
        Assert.Equal(Vector3.Zero, MoonDisk.Capture(lighting with { HasNightSources = false }).Radiance);
        Assert.Equal(Vector3.Zero, MoonDisk.Capture(lighting with { Lunar = Vector3.Zero, MoonPhase = 0 }).Radiance);
        Assert.Equal(Vector3.Zero, MoonDisk.Capture(lighting with { Lunar = Vector3.Zero, HorizonElevation = .2f }).Radiance);
        Assert.Throws<ArgumentException>(() => MoonDisk.Capture(lighting with { HorizonElevation = .2f }));
        Assert.Throws<ArgumentException>(() => MoonDisk.Capture(lighting with { MoonPhase = float.NaN }));
        Assert.Throws<ArgumentException>(() => MoonDisk.Capture(lighting with { Lunar = new(-1) }));
    }

    /// <summary>Horizon clipping is normalized to admitted power rather than silently reducing lunar transport a second time.</summary>
    [Theory]
    [InlineData(1f)] [InlineData(2f)] [InlineData(4f)]
    public void HorizonNormalizationRetainsPublishedPower(float phase)
    {
        // Arrange: use the selected phase with a horizon that clips the lower disk.
        var lighting = MoonDiskLightingFixture.Snapshot(phase) with { HorizonElevation = 0 };
        // Act: capture the normalized disk and integrate the visible projected area.
        var disk = MoonDisk.Capture(lighting);
        double integral = Integrate(disk);
        // Assert: horizon clipping does not reduce already admitted lunar transport a second time.
        Assert.InRange(Math.Abs(disk.Radiance.X * integral / lighting.Lunar.X - 1), 0, .008);
    }
    #endregion

    #region Private

    /// <summary>Integrates a Cartesian projected disk independently of the producer's polar quadrature.</summary>
    private static double Integrate(MoonDisk disk)
    {
        const int size = 256;
        double sum = 0, sine = Math.Sin(disk.AngularRadius);
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            double u = 2.0 * (x + .5) / size - 1, v = 2.0 * (y + .5) / size - 1;
            double radius = u * u + v * v;
            if (radius >= 1) continue;
            double worldY = disk.Direction.Y * Math.Sqrt(1 - sine * sine * radius)
                + sine * (disk.Right.Y * u + disk.Up.Y * v);
            if (worldY < Math.Sin(disk.Horizon)) continue;
            sum += Math.Max(0, disk.SurfaceLight.X * u + disk.SurfaceLight.Z * Math.Sqrt(1 - radius));
        }
        return sum * 4.0 / (size * size) * sine * sine;
    }
    #endregion
}
