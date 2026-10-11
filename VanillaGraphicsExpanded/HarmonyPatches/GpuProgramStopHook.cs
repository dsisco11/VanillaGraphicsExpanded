using HarmonyLib;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Completes VGE sampler ownership when the engine stops a contract-based graphics program.</summary>
[HarmonyPatch(typeof(ShaderProgramBase), nameof(ShaderProgramBase.Stop))]
internal static class GpuProgramStopHook
{
    #region Program retirement

    /// <summary>Redirects temporary base-typed VGE calls without invoking native engine stop side effects.</summary>
    [HarmonyPrefix]
    internal static bool Prefix(ShaderProgramBase __instance)
    {
        if (__instance is not GpuProgram program) return true;
        program.Stop();
        return false;
    }

    #endregion
}
