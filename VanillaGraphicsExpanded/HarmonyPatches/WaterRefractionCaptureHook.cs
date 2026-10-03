using HarmonyLib;
using VanillaGraphicsExpanded.PBR.Liquids;
using Vintagestory.GameContent;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Observes the first-person overwrite boundary while preserving the engine's original draw and order.</summary>
[HarmonyPatch(typeof(EntityPlayerShapeRenderer), nameof(EntityPlayerShapeRenderer.DoRender3DOpaque))]
internal static class WaterRefractionCaptureHook
{
    #region Public API
    /// <summary>Captures world inputs before the engine changes projection and writes first-person visibility depth.</summary>
    [HarmonyPrefix]
    internal static void Prefix(EntityPlayerShapeRenderer __instance, bool __1)
        => WaterRefractionCapture.BeforeOverlay(__instance, __1);
    #endregion
}
