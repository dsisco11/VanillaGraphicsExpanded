using HarmonyLib;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Routes engine-base activation of VGE shader owners through their input submission lifecycle.</summary>
[HarmonyPatch(typeof(ShaderProgramBase), nameof(ShaderProgramBase.Use))]
internal static class OwnedShaderSubmissionHook
{
    #region Engine callbacks
    /// <summary>Intercepts external base calls while allowing the owner's underlying engine activation once.</summary>
    [HarmonyPrefix]
    internal static bool Prefix(ShaderProgramBase __instance)
    {
        if (__instance is not GpuProgram owner || owner.IsActivatingEngine) return true;
        owner.Use();
        return false;
    }
    #endregion
}
