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
    internal static void Prefix()
    {
        if (ShaderRegistry.SupressShaderAndBufferReloads) return;
        ShaderDigestIndexCache.Clear();
        PBR.Tessellation.TerrainTessellationPrograms.BeginReload();
        // The engine destroys all registered programs before compiling this generation.
        // Composition must use the same snapshot even when an individual replacement fails.
        PBR.PbrShaderLightingMode.GenerationLumOnEnabled = ModSystems.ConfigModSystem.Config.LumOn.Enabled;
    }
}
