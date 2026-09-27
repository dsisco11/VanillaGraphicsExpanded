using HarmonyLib;
using VanillaGraphicsExpanded.PBR.Tessellation;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Adds runtime GLSL stages before ShaderProgram.Compile resolves the engine's named uniform setters.</summary>
[HarmonyPatch(typeof(ClientPlatformWindows), nameof(ClientPlatformWindows.CreateShaderProgram))]
internal static class TerrainTessellationLinkHook
{
    /// <summary>Leaves engine compilation status and the ordinary executable intact if the optional candidate fails.</summary>
    [HarmonyPostfix]
    internal static void Postfix(ShaderProgram program, bool __result)
    {
        if (__result) TerrainTessellationPrograms.Prepare(program);
    }
}
