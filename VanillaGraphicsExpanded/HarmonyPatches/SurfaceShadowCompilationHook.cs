using HarmonyLib;
using VanillaGraphicsExpanded.PBR.Shadows;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Tracks the shared surface interface at the existing native compilation boundary.</summary>
[HarmonyPatch(typeof(ShaderProgram), nameof(ShaderProgram.Compile))]
internal static class SurfaceShadowCompilationHook
{
    #region Private
    /// <summary>Withdraws the old capability before replacement, including compilation that fails or throws.</summary>
    [HarmonyPrefix]
    private static void Prefix(ShaderProgram __instance) => SurfaceShadowPrograms.Remove(__instance);

    /// <summary>Validates the final linked interface after native compilation succeeds.</summary>
    [HarmonyPostfix]
    private static void Postfix(ShaderProgram __instance, bool __result)
    {
        if (__result) SurfaceShadowPrograms.Register(__instance);
    }
    #endregion
}
