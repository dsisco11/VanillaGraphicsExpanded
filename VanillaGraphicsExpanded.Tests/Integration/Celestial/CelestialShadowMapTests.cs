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
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

namespace VanillaGraphicsExpanded.Tests.Integration.Celestial;

    /// <summary>Checks installed shadow routing and native owner patch compatibility.</summary>
    [Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class CelestialShadowMapTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API


/// <summary>All four installed owners accept the transpiler and replace only inspected celestial getters.</summary>
    [Fact]
    public void InstalledOwnersPatchAndRetainNativeProjectionWork()
    {
        // Arrange: establish this scenario's controlled inputs and starting state.

        using var dependencies = new EngineDependencyResolution();
        var targets = CelestialShadowMapPatches.TargetMethods().ToArray();
        // Act and assert: All four installed owners accept the transpiler and replace only inspected celestial getters.
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
    #endregion
}
