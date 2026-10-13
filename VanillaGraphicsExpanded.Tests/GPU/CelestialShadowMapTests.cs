using System.Collections.Immutable;
using System.Numerics;
using System.Reflection.Emit;
using HarmonyLib;
using Moq;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks installed shadow routing and the completed emitter's camera and horizon inputs.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class CelestialShadowMapTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>All four installed owners accept the transpiler and replace only inspected celestial getters.</summary>
    [Fact]
    public void InstalledOwnersPatchAndRetainNativeProjectionWork()
    {
        using var dependencies = new EngineDependencyResolution();
        var targets = CelestialShadowMapPatches.TargetMethods().ToArray();
        Assert.Equal(new[] {"OnRenderBefore", "OnRenderShadowNear", "OnRenderShadowFar", "PrepareForShadowRendering"},
            targets.Select(method => method.Name));
        var harmony = new Harmony($"vge.tests.celestial.shadow.{Guid.NewGuid():N}");
        try
        {
            harmony.CreateClassProcessor(typeof(CelestialShadowMapPatches)).Patch();
            foreach (var target in targets)
            {
                Assert.Single(Harmony.GetPatchInfo(target)!.Transpilers, patch => patch.owner == harmony.Id);
                var original = PatchProcessor.GetOriginalInstructions(target);
                var patched = CelestialShadowMapPatches.Transpiler(original.Select(instruction => new CodeInstruction(instruction)), target).ToArray();
                Assert.Equal(original.Count, patched.Length);
                Assert.DoesNotContain(patched, instruction => instruction.operand is System.Reflection.MethodInfo method
                    && method.Name is "get_SunPosition" or "get_MoonPosition" or "get_SunPositionNormalized" or "get_MoonPhaseBrightness");
                string required = target.Name switch
                {
                    "OnRenderBefore" => nameof(CelestialShadowMapPatches.GatingMoon),
                    "PrepareForShadowRendering" => nameof(CelestialShadowMapPatches.ShadowMoon),
                    _ => nameof(CelestialShadowMapPatches.ShadowDirection)
                };
                Assert.Contains(patched, instruction => instruction.opcode == OpCodes.Call
                    && instruction.operand is System.Reflection.MethodInfo method
                    && method.DeclaringType == typeof(CelestialShadowMapPatches) && method.Name == required);
            }
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }

    /// <summary>Native fallback remains exact; an admitted moon drives both camera branches and cascade depth extension.</summary>
    [Fact]
    public void PublishedMoonRoutesBothCameraBranchesAndExtentDirection()
    {
        EnsureContextValid();
        using var owner = new AtmosphereModSystem();
        var calendar = new Mock<IClientGameCalendar>();
        var originalSun = new Vec3f(50, 0, 0);
        var originalMoon = new Vec3f(0, 0, 50);
        var originalUnit = new Vec3f(1, 0, 0);
        calendar.SetupGet(value => value.SunPosition).Returns(originalSun);
        calendar.SetupGet(value => value.MoonPosition).Returns(originalMoon);
        calendar.SetupGet(value => value.SunPositionNormalized).Returns(originalUnit);
        calendar.SetupGet(value => value.MoonPhaseBrightness).Returns(.26f);
        var legacy = new AtmosphereLighting(-Vector3.UnitY, Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero,
            ImmutableArray.Create(0f, 0f, 0f, 1f)) {Width = 1, Height = 1};
        owner.Publish(legacy);
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
