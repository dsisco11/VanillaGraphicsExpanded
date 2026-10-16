using System;
using FluffyClouds;
using HarmonyLib;
using VanillaGraphicsExpanded.PBR.Clouds;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Separates native allocation lifetimes from reusable texture names.</summary>
[HarmonyPatch(typeof(CloudRendererMap), nameof(CloudRendererMap.InitCloudTiles))]
internal static class CloudMapAllocationHook
{
    #region Private
    /// <summary>Withdraws content certificates before native resource retirement.</summary>
    private static void Prefix(CloudRendererMap __instance, out CloudNativeMapAdapter? __state)
    {
        __state = CloudNativeRuntime.Active?.ForMap(__instance); __state?.BeginAllocation();
    }
    /// <summary>Validates storage after native replacement without treating it as initialized.</summary>
    private static void Postfix(CloudNativeMapAdapter? __state) => __state?.CompleteAllocation();
    #endregion
}

