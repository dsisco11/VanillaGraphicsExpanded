using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Identifies the engine solar quad, including its depth-tested occlusion query, without affecting other standard draws.</summary>
[HarmonyPatch]
internal static class AtmosphereSunDrawHook
{
    [ThreadStatic] internal static bool Active;

    #region Engine scope
    /// <summary>Both engine callbacks use Standard only for the solar quad; the moon has its own shader.</summary>
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(SystemRenderSunMoon), "OnRenderFrame3D");
        yield return AccessTools.Method(typeof(SystemRenderSunMoon), "OnRenderFrame3DPost");
    }

    /// <summary>Preserves nested callback state before admitting the solar draw.</summary>
    [HarmonyPrefix]
    internal static void Prefix(out bool __state)
    {
        __state = Active;
        Active = true;
    }

    /// <summary>Restores scope even when rendering throws.</summary>
    [HarmonyFinalizer]
    internal static void Finalizer(bool __state) => Active = __state;
    #endregion
}
