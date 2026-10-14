using System.Collections.Immutable;
using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks calibrated lunar phases and source selection without modifying solar transport.</summary>
public sealed class AtmosphereNightLightingTests
{
    #region Public API
    /// <summary>The producer retains only separately modeled airglow, without the obsolete combined stellar floor.</summary>
    [Theory]
    [InlineData(0f)] [InlineData(.5f)] [InlineData(1f)]
    public void BackgroundContainsAirglowOnlyWithOneWeatherFactor(float clouds)
    {
        var empty = Snapshot(-Vector3.UnitY,Vector3.Zero,0);
        var combined = AtmosphereNightLighting.Combine(empty,empty,new(-Vector3.UnitY,Vector3.UnitY,0),clouds);
        float expected = CelestialSource.AirglowLux/MathF.PI*(1-.9f*clouds);
        Assert.Equal(expected,combined.BackgroundRadiance);
        Assert.Equal(new Vector3(expected),combined.Horizon);
        Assert.Null(combined.CelestialSource);
        Assert.Equal(Vector3.Zero,combined.CelestialEnvironment);
        for(int i=0;i<combined.AerialRadiance.Length;i++)
            if((i&3)!=3) Assert.Equal(expected*empty.AerialAttenuation[i],combined.AerialRadiance[i]);
    }
    /// <summary>Phase wrapping preserves the physical full-moon reference and an exactly dark new moon.</summary>
    [Theory]
    [InlineData(0, 0)] [InlineData(8, 0)] [InlineData(-8, 0)]
    [InlineData(4, 1)] [InlineData(12, 1)] [InlineData(-4, 1)]
    public void PhaseWrapsAtEight(float phase, float expected)
    {
        Assert.Equal(expected, AtmosphereNightLighting.PhaseFraction(phase));
        Assert.Throws<ArgumentOutOfRangeException>(() => AtmosphereNightLighting.PhaseFraction(float.NaN));
    }

    /// <summary>Calibrated lunar scaling is linear, sunlight remains identical, and new-moon background remains nonzero.</summary>
    [Theory]
    [InlineData(0)] [InlineData(4)]
    public void CompositionPreservesSolarAndScalesLunarOnce(float phase)
    {
        var solar = Snapshot(Vector3.UnitX, new(100000), 10);
        var lunar = Snapshot(Vector3.UnitY, new(100000), 20);
        var combined = AtmosphereNightLighting.Combine(solar, lunar, new(Vector3.UnitX, Vector3.UnitY, phase), 0);
        Assert.Equal(solar.Solar, combined.Solar);
        Assert.Equal(solar.Sun, combined.DirectDirection);
        float expected = phase == 0 ? 0 : .1f;
        Assert.InRange(Math.Abs(Vector3.Dot(combined.Lunar, new(.2126f, .7152f, .0722f)) - expected), 0, 1e-5f);
        Assert.True(combined.Environment.X > solar.Environment.X);
        Assert.Equal(solar.AerialAttenuation, combined.AerialAttenuation);
        Assert.Equal(solar.SkyMie, combined.SkyMie);
        Assert.Equal(solar.Sky[3], combined.Sky[3]);
    }

    /// <summary>Only an extinguished sun and admitted visible lunar illuminance switch the shared shadow owner.</summary>
    [Fact]
    public void DirectSelectionRequiresAdmittedLunarIrradiance()
    {
        var snapshot = Snapshot(-Vector3.UnitY, Vector3.Zero, 0) with
            { HasNightSources = true, Moon = Vector3.UnitY, Lunar = new(.1f) };
        Assert.True(snapshot.MoonIsDirect);
        Assert.Equal(Vector3.UnitY, snapshot.DirectDirection);
        Assert.Equal(new Vector3(.1f), snapshot.DirectIrradiance);
        Assert.False((snapshot with { Lunar = Vector3.Zero }).MoonIsDirect);
        Assert.False((snapshot with { Sun = Vector3.UnitY, Solar = new(.0001f) }).MoonIsDirect);
        Assert.False((snapshot with { HasNightSources = false }).MoonIsDirect);
        Assert.Equal(new Vector3(.0001f), (snapshot with { Sun = Vector3.UnitY, Solar = new(.0001f) }).DirectIrradiance);
    }
    #endregion

    #region Private
    /// <summary>Builds a bounded common medium grid with distinct RGB transport and retained metadata.</summary>
    private static AtmosphereLighting Snapshot(Vector3 direction, Vector3 direct, float value)
    {
        var sky = ImmutableArray.CreateRange(Enumerable.Range(0, 4).SelectMany(_ => new[] {value, value, value, 1f}));
        var aerial = ImmutableArray.CreateRange(Enumerable.Range(0, 4 * AtmosphereAerialPerspective.Depth)
            .SelectMany(_ => new[] {value, value, value, 1f}));
        return new(direction, direct, new(value), new(value), Vector3.Zero, sky)
        {
            Width = 2, Height = 2, AerialRadiance = aerial,
            AerialAttenuation = ImmutableArray.CreateRange(Enumerable.Range(0, 4 * AtmosphereAerialPerspective.Depth)
                .SelectMany(_ => new[] {.2f, .3f, .4f, 1f}))
        };
    }
    #endregion
}
