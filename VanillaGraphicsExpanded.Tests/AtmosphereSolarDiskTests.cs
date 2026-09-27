using System.Collections.Immutable;
using System.Numerics;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR.Atmosphere;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks finite solar geometry, shared energy and engine draw scope ownership.</summary>
public sealed class AtmosphereSolarDiskTests
{
    #region Solar geometry
    /// <summary>Finite-disk extinction stays nonnegative and continuous while the emitter crosses the planetary limb.</summary>
    [Theory]
    [InlineData(.001f)]
    [InlineData(25f)]
    [InlineData(99f)]
    public void SunriseIrradianceIsBoundedAndContinuous(float altitude)
    {
        float horizon = AtmosphereSkyMapping.Horizon(altitude);
        float radius = AtmosphereSolarDisk.AngularRadius;
        float noon = AtmosphereModel.SolarIrradiance(Vector3.UnitY, altitude, 1).Length();
        Vector3 previous = Vector3.Zero;
        for (int i = 0; i <= 100; i++)
        {
            float elevation = horizon + radius * (i / 50f - 1.01f);
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
        float radius = AtmosphereSolarDisk.AngularRadius;
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
        const float horizon = -.1f;
        float radius = AtmosphereSolarDisk.AngularRadius, previous = 0;
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
        float elevation = offset * AtmosphereSolarDisk.AngularRadius;
        Vector3 irradiance = new(.4f, .2f, .1f);
        var lighting = new AtmosphereLighting(new(MathF.Cos(elevation), MathF.Sin(elevation), 0), irradiance,
            Vector3.Zero, Vector3.Zero, Vector3.Zero, ImmutableArray<float>.Empty);
        float visibility = AtmosphereSolarDisk.Visibility(elevation, 0);
        float sine = MathF.Sin(AtmosphereSolarDisk.AngularRadius);
        Vector3 integrated = AtmosphereSolarDisk.Radiance(lighting) * (visibility * MathF.PI * sine * sine);
        Assert.InRange(Vector3.Distance(integrated, irradiance), 0, 1e-6f);
    }

    /// <summary>Clear sunset attenuation reddens direct solar light relative to midday.</summary>
    [Fact]
    public void TwilightIsFiniteAndRedderThanNoon()
    {
        Vector3 noon = AtmosphereModel.SolarIrradiance(Vector3.UnitY, .001f, 1);
        Vector3 sunset = AtmosphereModel.SolarIrradiance(Vector3.UnitX, .001f, 1);
        Assert.True(sunset.X > 0 && sunset.Z >= 0 && float.IsFinite(sunset.Length()));
        Assert.True(sunset.X / MathF.Max(sunset.Z, 1e-20f) > noon.X / noon.Z);
        Assert.True(sunset.Length() < noon.Length());
    }
    #endregion

    #region Draw lifetime
    /// <summary>Nested engine callbacks and exception unwinding restore the previous solar classification.</summary>
    [Fact]
    public void DrawScopeRestoresNestedAndFailedCallbacks()
    {
        bool original = AtmosphereSunDrawHook.Active;
        try
        {
            AtmosphereSunDrawHook.Active = false;
            AtmosphereSunDrawHook.Prefix(out bool outer);
            Assert.True(AtmosphereSunDrawHook.Active);
            AtmosphereSunDrawHook.Prefix(out bool inner);
            try { throw new InvalidOperationException("draw failed"); }
            catch (InvalidOperationException) { }
            finally { AtmosphereSunDrawHook.Finalizer(inner); }
            Assert.True(AtmosphereSunDrawHook.Active);
            AtmosphereSunDrawHook.Finalizer(outer);
            Assert.False(AtmosphereSunDrawHook.Active);
        }
        finally { AtmosphereSunDrawHook.Active = original; }
    }
    #endregion
}
