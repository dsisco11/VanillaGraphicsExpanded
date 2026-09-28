using HarmonyLib;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Labels every engine shader compilation without maintaining a pass-name allowlist.</summary>
[HarmonyPatch(typeof(ShaderProgram), nameof(ShaderProgram.Compile))]
internal static class EngineShaderDebugLabelsHook
{
    #region Compilation lifecycle
    /// <summary>Labels successfully compiled objects; recovery recompilation follows this same hook.</summary>
    [HarmonyPostfix]
    internal static void Postfix(ShaderProgram __instance, bool __result)
    {
        if (__result) EngineShaderDebugLabels.Apply(__instance);
    }
    #endregion
}
