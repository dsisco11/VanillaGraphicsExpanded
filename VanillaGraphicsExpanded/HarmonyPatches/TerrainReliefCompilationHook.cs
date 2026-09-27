using HarmonyLib;
using VanillaGraphicsExpanded.PBR.Materials;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Publishes the relief binding contract after engine uniform collection.</summary>
[HarmonyPatch(typeof(ShaderProgram), nameof(ShaderProgram.Compile))]
internal static class TerrainReliefCompilationHook
{
    #region Compilation lifecycle
    /// <summary>Failed replacement compilation does not overwrite a still-installed contract.</summary>
    [HarmonyPostfix]
    internal static void Postfix(ShaderProgram __instance, bool __result)
    {
        if (__result) TerrainReliefBindings.Register(__instance);
    }
    #endregion
}
