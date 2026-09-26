using HarmonyLib;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Invalidates digest indexes before the engine replaces assets and recompiles registered programs.</summary>
[HarmonyPatch(typeof(ShaderRegistry), nameof(ShaderRegistry.ReloadShaders))]
internal static class ShaderDigestReloadHook
{
    /// <summary>Runs before compilation; the public reload event occurs too late for registered shaders.</summary>
    [HarmonyPrefix]
    internal static void Prefix() => ShaderDigestIndexCache.Clear();
}
