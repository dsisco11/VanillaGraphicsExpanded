using HarmonyLib;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Refreshes the atmospheric binding contract for each engine shader compilation.</summary>
[HarmonyPatch(typeof(ShaderProgram), nameof(ShaderProgram.Compile))]
internal static class AtmosphereShaderCompilationHook
{
    #region Compilation lifecycle
    /// <summary>Removes old metadata even when the replacement compilation fails or throws.</summary>
    [HarmonyPrefix]
    internal static void Prefix(ShaderProgram __instance) => AtmosphereProgramBindings.Remove(__instance);

    /// <summary>Resolves active allowlisted inputs once, after the engine has populated its linked uniform interface.</summary>
    [HarmonyPostfix]
    internal static void Postfix(ShaderProgram __instance, bool __result)
    {
        if (__result) AtmosphereProgramBindings.Register(__instance);
    }
    #endregion
}
