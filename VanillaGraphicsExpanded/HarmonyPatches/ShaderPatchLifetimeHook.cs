using HarmonyLib;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Releases recovery source when the engine disposes its shader program.</summary>
[HarmonyPatch(typeof(ShaderProgramBase), nameof(ShaderProgramBase.Dispose))]
internal static class ShaderPatchLifetimeHook
{
    /// <summary>Leaves GPU disposal to the engine and removes only retained source.</summary>
    [HarmonyPrefix]
    private static void Prefix(ShaderProgramBase __instance)
    {
        if (__instance is ShaderProgram program) ShaderPatchRecovery.Forget(program);
    }
}
