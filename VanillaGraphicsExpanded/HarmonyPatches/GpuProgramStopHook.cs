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

    /// <summary>Releases explicit contract slots omitted by the engine's source-based custom sampler list.</summary>
    [HarmonyPostfix]
    internal static void Postfix(ShaderProgramBase __instance)
    {
        // Stop is sealed in the engine. Hooking it also covers calls through the
        // engine's current-program reference and VGE's nested program scopes.
        if (__instance is not GpuProgram program) return;
        program.ProgramLayout.ReleaseSamplerBindings();
        StateCache.Current.NotifyProgramBound(0);
    }

    #endregion
}
