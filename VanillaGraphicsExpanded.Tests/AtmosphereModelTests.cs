using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks physical trends and geometric invariants of the bounded atmosphere integrator.</summary>
public sealed class AtmosphereModelTests
{
    #region Physical response
    /// <summary>Representative altitudes, haze and solar/view elevations remain finite and nonnegative.</summary>
    [Theory]
    [InlineData(.001f, .1f)]
    [InlineData(.001f, 8f)]
    [InlineData(2f, 1f)]
    [InlineData(50f, 4f)]
    [InlineData(99f, 1f)]
    public void RepresentativeRaysRemainFinite(float altitude, float aerosol)
    {
        foreach (Vector3 sun in new[] { Vector3.UnitY, Vector3.UnitX, -Vector3.UnitY })
            foreach (Vector3 direction in new[] { Vector3.UnitY, Vector3.UnitX, -Vector3.UnitY, Vector3.Normalize(new Vector3(1,.01f,0)) })
                AssertFinite(AtmosphereModel.Radiance(direction, sun, altitude, aerosol));
        AssertFinite(AtmosphereModel.SolarIrradiance(Vector3.UnitY, altitude, aerosol));
    }

    /// <summary>Molecular scattering produces a blue zenith away from the forward solar peak.</summary>
    [Fact]
    public void MiddayZenithIsBlue()
    {
        Vector3 radiance = AtmosphereModel.Radiance(Vector3.UnitY, Vector3.Normalize(new Vector3(1,1,0)), .001f, 1);
        Assert.True(radiance.Z > radiance.Y && radiance.Y > radiance.X, radiance.ToString());
    }

    /// <summary>A low sun loses more blue light than an overhead sun and the planet occludes downward solar rays.</summary>
    [Fact]
    public void HorizonReddeningAndPlanetOcclusion()
    {
        Vector3 noon = AtmosphereModel.SolarIrradiance(Vector3.UnitY, .001f, 1);
        Vector3 horizon = AtmosphereModel.SolarIrradiance(Vector3.Normalize(new Vector3(1,.02f,0)), .001f, 1);
        Assert.True(horizon.Z / horizon.X < noon.Z / noon.X);
        Assert.True(horizon.X < noon.X);
        Assert.Equal(Vector3.Zero, AtmosphereModel.SolarIrradiance(-Vector3.UnitY, .001f, 1));
    }

    /// <summary>Less atmosphere above an observer transmits more direct sunlight, while more aerosol attenuates it.</summary>
    [Fact]
    public void AltitudeAndAerosolsControlTransmission()
    {
        Vector3 low = AtmosphereModel.SolarIrradiance(Vector3.UnitY, .001f, 1);
        Vector3 high = AtmosphereModel.SolarIrradiance(Vector3.UnitY, 20, 1);
        Vector3 haze = AtmosphereModel.SolarIrradiance(Vector3.UnitY, .001f, 8);
        Assert.True(high.X > low.X && high.Y > low.Y && high.Z > low.Z);
        Assert.True(haze.X < low.X && haze.Y < low.Y && haze.Z < low.Z);
    }

    /// <summary>Overhead transmission agrees with analytically integrated density columns within the bounded quadrature budget.</summary>
    [Theory]
    [InlineData(1f)]
    [InlineData(8f)]
    public void VerticalTransmissionMatchesAnalyticDensityColumns(float aerosol)
    {
        const double altitude = .001;
        double rayleighColumn = 8 * (Math.Exp(-altitude / 8) - Math.Exp(-100d / 8));
        double aerosolColumn = 1.2 * (Math.Exp(-altitude / 1.2) - Math.Exp(-100d / 1.2));
        double[] rayleigh = [.005802, .013558, .0331], ozone = [.000650, .001881, .000085], solar = [1.474,1.8504,1.91198];
        Vector3 actual = AtmosphereModel.SolarIrradiance(Vector3.UnitY, (float)altitude, aerosol);
        for (int channel = 0; channel < 3; channel++)
        {
            // The triangular ozone layer has an exact 15 km integrated column.
            double expected = solar[channel] * Math.Exp(-rayleigh[channel] * rayleighColumn - .004440 * aerosol * aerosolColumn - ozone[channel] * 15);
            Assert.InRange(Math.Abs(actual[channel] / expected - 1), 0, .01);
        }
    }
    #endregion

    #region Geometric invariants
    /// <summary>Azimuth rotations preserve a spherical atmosphere when sun and viewing direction rotate together.</summary>
    [Fact]
    public void JointAzimuthRotationPreservesRadiance()
    {
        Vector3 view = Vector3.Normalize(new Vector3(.8f,.3f,.2f));
        Vector3 sun = Vector3.Normalize(new Vector3(-.2f,.7f,.6f));
        Quaternion rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1.1f);
        Vector3 expected = AtmosphereModel.Radiance(view, sun, 1, 2);
        Vector3 actual = AtmosphereModel.Radiance(Vector3.Transform(view, rotation), Vector3.Transform(sun, rotation), 1, 2);
        Assert.InRange(Vector3.Distance(expected, actual), 0, .0001f);
    }

    /// <summary>Rejects nonfinite or negative RGB outputs without allowing NaN through comparisons.</summary>
    private static void AssertFinite(Vector3 value)
    {
        Assert.True(float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z), value.ToString());
        Assert.True(value.X >= 0 && value.Y >= 0 && value.Z >= 0, value.ToString());
    }
    #endregion
}
