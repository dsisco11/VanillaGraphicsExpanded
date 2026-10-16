using HarmonyLib;
using VanillaGraphicsExpanded.PBR.Clouds;
using Vintagestory.API.Client;
using Vintagestory.GameContent;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Replaces only the selected native Simple draw; native mesh, reload and weather owners remain intact.</summary>
[HarmonyPatch(typeof(CloudRenderer), nameof(CloudRenderer.OnRenderFrame))]
internal static class CloudSimpleDrawHook
{
    #region Public API
    /// <summary>Executes the owned callback once or leaves the original native body entirely responsible.</summary>
    internal static bool Prefix(CloudRenderer __instance, float deltaTime, EnumRenderStage stage) =>
        CloudNativeRuntime.Active?.RenderSimple(__instance, deltaTime, stage) != true;
    #endregion
}
