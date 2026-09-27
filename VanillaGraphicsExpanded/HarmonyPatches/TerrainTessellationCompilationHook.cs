using HarmonyLib;
using VanillaGraphicsExpanded.PBR.Tessellation;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Selects the terrain tessellation define before engine stage compilation.</summary>
[HarmonyPatch(typeof(ShaderProgram), nameof(ShaderProgram.Compile))]
internal static class TerrainTessellationCompilationHook
{
    #region Compilation lifecycle
    /// <summary>Publishes the complete macro choice for both engine stages before either is compiled.</summary>
    [HarmonyPrefix]
    internal static void Prefix(ShaderProgram __instance) => TerrainTessellationPatches.Configure(__instance);

    #endregion
}
