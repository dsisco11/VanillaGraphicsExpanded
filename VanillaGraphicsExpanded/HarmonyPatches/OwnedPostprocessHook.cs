using HarmonyLib;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using Vintagestory.Client.NoObf;
namespace VanillaGraphicsExpanded.HarmonyPatches;
/// <summary>Hands complete HDR scene postprocessing to VGE while preserving the engine's presentation schedule.</summary>
[HarmonyPatch(typeof(ClientPlatformWindows),nameof(ClientPlatformWindows.RenderPostprocessingEffects))]
internal static class OwnedPostprocessHook
{
    #region Public API
    /// <summary>Suppresses the original scene pass only after the owned replacement has completed successfully.</summary>
    [HarmonyPrefix]
    internal static bool Prefix(ClientPlatformWindows __instance,float[]? __0)
        => !PostprocessPipeline.ReplaceEnginePass(__instance,__0);
    #endregion
}
