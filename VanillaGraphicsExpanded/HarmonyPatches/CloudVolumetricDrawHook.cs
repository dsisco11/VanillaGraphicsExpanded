using FluffyClouds;
using HarmonyLib;
using VanillaGraphicsExpanded.PBR.Clouds;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Gates the native volume draw directly so native settings watchers retain their registration authority.</summary>
[HarmonyPatch(typeof(CloudRendererVolumetric), nameof(CloudRendererVolumetric.OnRenderFrame))]
internal static class CloudVolumetricDrawHook
{
    #region Private
    /// <summary>Runs native drawing unless a functioning selected VGE consumer owns this exact native draw's replacement.</summary>
    private static bool Prefix(CloudRendererVolumetric __instance) =>
        CloudNativeRuntime.Active?.SuppressVolume(__instance) != true;
    #endregion
}
