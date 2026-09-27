using HarmonyLib;
using VanillaGraphicsExpanded.PBR.Tessellation;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Removes patch-draw metadata when an engine terrain object is disposed.</summary>
[HarmonyPatch(typeof(ShaderProgramBase), nameof(ShaderProgramBase.Dispose))]
internal static class TerrainTessellationLifetimeHook
{
    /// <summary>Removes metadata without taking ownership of engine resources.</summary>
    [HarmonyPrefix]
    private static void Prefix(ShaderProgramBase __instance) => TerrainTessellationPrograms.Forget(__instance);
}
