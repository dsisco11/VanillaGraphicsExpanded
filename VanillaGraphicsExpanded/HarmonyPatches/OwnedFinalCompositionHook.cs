using HarmonyLib;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using Vintagestory.Client.NoObf;
namespace VanillaGraphicsExpanded.HarmonyPatches;
/// <summary>Replaces scene final composition while preserving the engine's overlay and presentation schedule.</summary>
[HarmonyPatch(typeof(ClientPlatformWindows),nameof(ClientPlatformWindows.RenderFinalComposition))]
internal static class OwnedFinalCompositionHook
{
    #region Public API
    /// <summary>Suppresses the engine scene shader only through the explicit owned scene handoff.</summary>
    [HarmonyPrefix]
    internal static bool Prefix(ClientPlatformWindows __instance)=>!PostprocessPipeline.ReplaceFinalPass(__instance);
    #endregion
}
