using System.Collections.Immutable;
using System.Numerics;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR.Atmosphere;

namespace VanillaGraphicsExpanded.Tests.Unit.Atmosphere;

/// <summary>Checks finite solar geometry, shared energy and engine draw scope ownership.</summary>
public sealed class SolarGeometryAndDrawScopeRulesTests
{
    #region Public API
    /// <summary>The solar disk uses half the nominal vanilla bright-core angular diameter, excluding the halo.</summary>
    [Fact]
    public void DiameterIsHalfVanillaBrightCore()
    {
        // Arrange the vanilla bright-core radius from its authored pixel and distance dimensions.
        float vanillaAngularRadius = MathF.Atan((48f * .04f) / (50f - 128f * .04f));
        // Act by reading the production solar-disk angular radius and doubling it to compare diameters.
        // Assert the production disk diameter is half that bright-core angular diameter.
        Assert.InRange(MathF.Abs(AtmosphereSolarDisk.AngularRadius * 2f - vanillaAngularRadius), 0, 1e-8f);
    }

    /// <summary>Finite-disk extinction stays nonnegative and continuous while the emitter crosses the planetary limb.</summary>
    [Theory]
    [InlineData(.001f)]
    [InlineData(25f)]
    [InlineData(99f)]
    public void SunriseIrradianceIsBoundedAndContinuous(float altitude)
    {
        // Arrange: derive the horizon and disk radius for the requested observer altitude.
        // Act: integrate overhead irradiance and sweep disk positions around the horizon.
        float horizon = AtmosphereSkyMapping.Horizon(altitude);
        float radius = AtmosphereSolarDisk.AngularRadius;
        float noon = AtmosphereModel.SolarIrradiance(Vector3.UnitY, altitude, 1).Length();
        Vector3 previous = Vector3.Zero;
        // Assert: check each swept position for nonnegative finite energy and bounded step-to-step change.
        for (int i = 0; i <= 100; i++)
        {
            float elevation = horizon + radius * (i / 50f - 1.01f);
            // Act: evaluate irradiance at this step as the solar disk crosses the horizon.
            Vector3 solar = AtmosphereModel.SolarIrradiance(new(MathF.Cos(elevation), MathF.Sin(elevation), 0), altitude, 1);
            Assert.True(float.IsFinite(solar.Length()) && solar.X >= 0 && solar.Y >= 0 && solar.Z >= 0);
            if (i == 0) Assert.Equal(Vector3.Zero, solar);
            Assert.True(Vector3.Distance(previous, solar) <= noon * .05f, $"altitude={altitude}, step={i}, previous={previous}, solar={solar}");
            previous = solar;
        }
        Assert.True(previous.Length() > 0);
    }

    /// <summary>Thin grazing segments retain finite nonnegative area and a centroid inside the surviving disk.</summary>
    [Fact]
    public void GrazingSegmentsStayFinite()
    {
        // Arrange the authored finite-disk radius for symmetric thin-segment grazing probes.
        float radius = AtmosphereSolarDisk.AngularRadius;
        // Act and Assert: probe thin grazing segments and keep each centroid inside its visible area.
        for (int exponent = 2; exponent <= 7; exponent++)
        foreach (float sign in new[] { -1f, 1f })
        {
            float elevation = sign * (1 - MathF.Pow(10, -exponent)) * radius;
            float visible = AtmosphereSolarDisk.Visibility(elevation, 0);
            Assert.True(float.IsFinite(visible) && visible > 0 && visible <= 1, $"e={elevation:G9}, area={visible:G9}");
            float centroid = AtmosphereSolarDisk.VisibleElevation(elevation, 0, visible);
            Assert.True(float.IsFinite(centroid) && centroid >= -1e-7f && centroid <= elevation + radius + 1e-7f,
                $"e={elevation:G9}, area={visible:G9}, centroid={centroid:G9}");
        }
    }

    /// <summary>Coverage and the surviving segment centroid remain bounded through sunrise.</summary>
    [Fact]
    public void VisibleSegmentIsContinuousAndBounded()
    {
        // Arrange a fixed horizon and disk radius for the sunrise coverage sweep.
        const float horizon = -.1f;
        float radius = AtmosphereSolarDisk.AngularRadius, previous = 0;
        // Act and Assert: sweep the disk through sunrise and constrain coverage and centroid bounds.
        for (int i = 0; i <= 200; i++)
        {
            float elevation = horizon + radius * (i / 100f - 1);
            float visibility = AtmosphereSolarDisk.Visibility(elevation, horizon);
            Assert.InRange(visibility, previous - 1e-6f, 1);
            Assert.InRange(visibility - previous, -1e-6f, .007f);
            if (visibility > 1e-5f)
                Assert.InRange(AtmosphereSolarDisk.VisibleElevation(elevation, horizon, visibility), horizon - 1e-6f, elevation + radius + 1e-6f);
            previous = visibility;
        }
        Assert.Equal(.5f, AtmosphereSolarDisk.Visibility(horizon, horizon));
    }

    /// <summary>Integrating disk radiance reproduces the published normal irradiance at each visibility.</summary>
    [Theory]
    [InlineData(-.5f)]
    [InlineData(0f)]
    [InlineData(.5f)]
    [InlineData(2f)]
    public void RadiancePreservesSolarEnergy(float offset)
    {
        // Arrange each theory-row offset as a solar elevation and supply a known RGB irradiance.
        float elevation = offset * AtmosphereSolarDisk.AngularRadius;
        Vector3 irradiance = new(.4f, .2f, .1f);
        var lighting = new AtmosphereLighting(new(MathF.Cos(elevation), MathF.Sin(elevation), 0), irradiance,
            Vector3.Zero, Vector3.Zero, Vector3.Zero, ImmutableArray<float>.Empty);
        float visibility = AtmosphereSolarDisk.Visibility(elevation, 0);
        float sine = MathF.Sin(AtmosphereSolarDisk.AngularRadius);
        // Act by integrating disk radiance over the measured visible solid angle.
        Vector3 integrated = AtmosphereSolarDisk.Radiance(lighting) * (visibility * MathF.PI * sine * sine);
        // Assert the integrated disk reproduces the supplied normal irradiance.
        Assert.InRange(Vector3.Distance(integrated, irradiance), 0, 1e-6f);
    }

    /// <summary>Clear sunset attenuation reddens direct solar light relative to midday.</summary>
    [Fact]
    public void TwilightIsFiniteAndRedderThanNoon()
    {
        // Arrange: compare overhead midday sunlight with a horizontal sunset direction.
        // Act: integrate both direct-solar conditions.
        Vector3 noon = AtmosphereModel.SolarIrradiance(Vector3.UnitY, .001f, 1);
        Vector3 sunset = AtmosphereModel.SolarIrradiance(Vector3.UnitX, .001f, 1);
        // Assert sunset remains finite, redder, and dimmer than the overhead reference.
        Assert.True(sunset.X > 0 && sunset.Z >= 0 && float.IsFinite(sunset.Length()));
        Assert.True(sunset.X / MathF.Max(sunset.Z, 1e-20f) > noon.X / noon.Z);
        Assert.True(sunset.Length() < noon.Length());
    }
    #endregion
}
