using HarmonyLib;
using VanillaGraphicsExpanded.PBR.Shadows;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Binds the shared surface provider after native shader activation without changing native geometry ownership.</summary>
[HarmonyPatch(typeof(ShaderProgramBase),nameof(ShaderProgramBase.Use))]
internal static class SurfaceShadowBindingHook
{
    #region Private
    /// <summary>Publishes the same current-light and rendered-shadow snapshot to every successfully linked forward material.</summary>
    [HarmonyPostfix]
    private static void Postfix(ShaderProgramBase __instance)
    {
        if(!SurfaceShadowPrograms.Participates(__instance.PassName))return;
        if(SurfaceShadowPrograms.Has(__instance))SurfaceShadowBindings.BindNative(SurfaceShadowBindings.CurrentInputs);
    }
    #endregion
}
