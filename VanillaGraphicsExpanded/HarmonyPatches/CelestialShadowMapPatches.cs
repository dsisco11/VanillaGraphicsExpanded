using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Feeds the completed celestial emitter into the engine's existing shadow camera and cascades.</summary>
[HarmonyPatch]
internal static class CelestialShadowMapPatches
{
    #region Public API
    /// <summary>Targets only the inspected shadow owner methods; no additional render pass or target is introduced.</summary>
    internal static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(SystemRenderShadowMap), "OnRenderBefore", [typeof(float)]);
        yield return AccessTools.Method(typeof(SystemRenderShadowMap), "OnRenderShadowNear", [typeof(float)]);
        yield return AccessTools.Method(typeof(SystemRenderShadowMap), "OnRenderShadowFar", [typeof(float)]);
        yield return AccessTools.Method(typeof(SystemRenderShadowMap), "PrepareForShadowRendering",
            [typeof(double), typeof(EnumFrameBuffer), typeof(float)]);
    }

    /// <summary>Replaces celestial getter calls locally while retaining native projection, fog and cascade lifecycle.</summary>
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        bool gating = __originalMethod.Name == "OnRenderBefore";
        foreach (var instruction in instructions)
        {
            if (instruction.operand is MethodInfo method && (instruction.opcode == OpCodes.Callvirt || instruction.opcode == OpCodes.Call))
            {
                string? replacement = method.Name switch
                {
                    "get_SunPosition" => gating ? nameof(GatingSun) : nameof(ShadowSun),
                    "get_MoonPosition" => gating ? nameof(GatingMoon) : nameof(ShadowMoon),
                    "get_SunPositionNormalized" => nameof(ShadowDirection),
                    "get_MoonPhaseBrightness" when gating => nameof(ShadowPhase),
                    _ => null
                };
                if (replacement is not null && method.DeclaringType is { } owner
                    && typeof(IGameCalendar).IsAssignableFrom(owner))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(CelestialShadowMapPatches), replacement);
                }
            }
            yield return instruction;
        }
    }

    /// <summary>Uses the same completed direction for either branch of the native shadow-camera selection.</summary>
    internal static Vec3f ShadowSun(IClientGameCalendar calendar)
    {
        var lighting = AtmosphereModSystem.Lighting;
        return lighting is { HasNightSources: true } ? Position(lighting.DirectDirection) : calendar.SunPosition;
    }

    /// <summary>Retains native lunar fallback while making its selected branch match the physical emitter.</summary>
    internal static Vec3f ShadowMoon(IClientGameCalendar calendar)
    {
        var lighting = AtmosphereModSystem.Lighting;
        return lighting is { HasNightSources: true } ? Position(lighting.DirectDirection) : calendar.MoonPosition;
    }

    /// <summary>Calculates cascade depth extent using the selected emitter rather than a below-ground sun.</summary>
    internal static Vec3f ShadowDirection(IClientGameCalendar calendar)
    {
        var lighting = AtmosphereModSystem.Lighting;
        return lighting is { HasNightSources: true }
            ? new(lighting.DirectDirection.X, lighting.DirectDirection.Y, lighting.DirectDirection.Z)
            : calendar.SunPositionNormalized;
    }

    /// <summary>Allows native sun-elevation gating only while the completed sun owns direct illumination.</summary>
    internal static Vec3f GatingSun(IClientGameCalendar calendar)
    {
        var lighting = AtmosphereModSystem.Lighting;
        if (lighting is not { HasNightSources: true }) return calendar.SunPosition;
        return !lighting.MoonIsDirect && lighting.DirectIrradiance != System.Numerics.Vector3.Zero
            ? Position(lighting.DirectDirection) : new(0, -50, 0);
    }

    /// <summary>Allows native lunar gating only while the illuminated moon owns the shared shadow map.</summary>
    internal static Vec3f GatingMoon(IClientGameCalendar calendar)
    {
        var lighting = AtmosphereModSystem.Lighting;
        if (lighting is not { HasNightSources: true }) return calendar.MoonPosition;
        return lighting.MoonIsDirect ? Position(lighting.Moon) : new(0, -50, 0);
    }

    /// <summary>Keeps phase energy in photometry rather than additionally weakening shadow contrast.</summary>
    internal static float ShadowPhase(IGameCalendar calendar)
    {
        var lighting = AtmosphereModSystem.Lighting;
        return lighting is { HasNightSources: true } ? (lighting.MoonIsDirect ? .6f : 0) : calendar.MoonPhaseBrightness;
    }
    #endregion

    #region Private
    /// <summary>Matches the native celestial camera-position length while preserving the published direction.</summary>
    private static Vec3f Position(System.Numerics.Vector3 direction) => new(direction.X * 50, direction.Y * 50, direction.Z * 50);
    #endregion
}
