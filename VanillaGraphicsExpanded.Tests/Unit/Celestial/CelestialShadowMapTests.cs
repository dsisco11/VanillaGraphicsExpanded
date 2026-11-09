using System.Collections.Immutable;
using System.Numerics;
using System.Reflection.Emit;
using HarmonyLib;
using Moq;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR.Atmosphere;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.Tests.Unit.Celestial;

    /// <summary>Checks celestial shadow routing for published moon state and camera inputs.</summary>
    [Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class CelestialShadowMapTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API


/// <summary>Native fallback remains exact; an admitted moon drives both camera branches and cascade depth extension.</summary>
    [Fact]
    public void PublishedMoonRoutesBothCameraBranchesAndExtentDirection()
    {
        // Arrange: establish this scenario's controlled inputs and starting state.

        EnsureContextValid();
        using var owner = new AtmosphereModSystem();
        var calendar = new Mock<IClientGameCalendar>();
        var originalSun = new Vec3f(50, 0, 0);
        var originalMoon = new Vec3f(0, 0, 50);
        var originalUnit = new Vec3f(1, 0, 0);
        // Arrange the engine calendar values used by native fallback routing.
        calendar.SetupGet(value => value.SunPosition).Returns(originalSun);
        calendar.SetupGet(value => value.MoonPosition).Returns(originalMoon);
        calendar.SetupGet(value => value.SunPositionNormalized).Returns(originalUnit);
        calendar.SetupGet(value => value.MoonPhaseBrightness).Returns(.26f);
        var legacy = new AtmosphereLighting(-Vector3.UnitY, Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero,
            ImmutableArray.Create(0f, 0f, 0f, 1f)) {Width = 1, Height = 1};
        // Act: publish legacy lighting and evaluate both camera-routing branches.
        owner.Publish(legacy);
        // Assert the fallback still returns the engine calendar values.
        Assert.Same(originalSun, CelestialShadowMapPatches.ShadowSun(calendar.Object));
        Assert.Same(originalMoon, CelestialShadowMapPatches.ShadowMoon(calendar.Object));
        Assert.Same(originalUnit, CelestialShadowMapPatches.ShadowDirection(calendar.Object));
        Assert.Equal(.26f, CelestialShadowMapPatches.ShadowPhase(calendar.Object));
        var moon = legacy with
        {
            HasNightSources = true, Moon = Vector3.UnitY, Lunar = new(.1f),
            SkyMoonMie = ImmutableArray.Create(0f, 0f, 0f, 1f),
            AerialMoonMie = ImmutableArray.Create(0f, 0f, 0f, 1f)
        };
        // Act: admit a moon source, then remove its lunar energy to exercise both published states.
        owner.Publish(moon);
        foreach (var actual in new[] {CelestialShadowMapPatches.ShadowSun(calendar.Object), CelestialShadowMapPatches.ShadowMoon(calendar.Object)})
            Assert.Equal(new Vec3f(0, 50, 0), actual);
        Assert.Equal(new Vec3f(0, 1, 0), CelestialShadowMapPatches.ShadowDirection(calendar.Object));
        Assert.Equal(new Vec3f(0, -50, 0), CelestialShadowMapPatches.GatingSun(calendar.Object));
        Assert.Equal(new Vec3f(0, 50, 0), CelestialShadowMapPatches.GatingMoon(calendar.Object));
        Assert.Equal(.6f, CelestialShadowMapPatches.ShadowPhase(calendar.Object));
        owner.Publish(moon with { Lunar = Vector3.Zero });
        Assert.Equal(new Vec3f(0, -50, 0), CelestialShadowMapPatches.GatingMoon(calendar.Object));
        Assert.Equal(0, CelestialShadowMapPatches.ShadowPhase(calendar.Object));
    }
    #endregion
}
