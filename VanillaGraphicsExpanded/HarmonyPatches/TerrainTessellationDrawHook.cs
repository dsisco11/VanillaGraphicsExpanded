using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using VanillaGraphicsExpanded.PBR.Tessellation;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Changes topology only at the engine's grouped terrain submission, preserving indices and buffer ownership.</summary>
[HarmonyPatch(typeof(ClientPlatformWindows), nameof(ClientPlatformWindows.RenderMesh),
    [typeof(MeshRef), typeof(int[]), typeof(int[]), typeof(int), typeof(bool)])]
internal static class TerrainTessellationDrawHook
{
    #region Scoped patch state
    /// <summary>Saves fixed-function patch state only when an installed tessellated program is active.</summary>
    [HarmonyPrefix]
    internal static void Prefix(out int? __state)
    {
        __state = null;
        if (!TerrainTessellationPrograms.Active) return;
        TerrainDisplacementRuntime.Bind();
        var cache = GlStateCache.Current;
        __state = cache.PatchVertices;
        cache.SetPatchVertices(3);
    }

    /// <summary>Restores patch size on normal completion and exceptional engine draw exits.</summary>
    [HarmonyFinalizer]
    internal static void Finalizer(int? __state)
    {
        if (__state is { } previous) GlStateCache.Current.SetPatchVertices(previous);
    }
    #endregion

    #region Draw interception
    /// <summary>Requires both known topology operands, rejecting engine changes before installing a partial patch.</summary>
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var field = AccessTools.Field(typeof(VAO), nameof(VAO.drawMode));
        TerrainTessellationPrograms.DrawHookAvailable = code.Count(i => i.opcode == OpCodes.Ldfld && Equals(i.operand, field)) == 2;
        if (!TerrainTessellationPrograms.DrawHookAvailable)
        {
            TerrainTessellationPrograms.Log?.Invoke("[VGE] Terrain tessellation disabled: unsupported grouped draw layout.");
            foreach (var instruction in code) yield return instruction;
            yield break;
        }
        var select = AccessTools.Method(typeof(TerrainTessellationPrograms), nameof(TerrainTessellationPrograms.Topology));
        foreach (var instruction in code)
        {
            yield return instruction;
            if (instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, field))
                yield return new CodeInstruction(OpCodes.Call, select);
        }
    }
    #endregion
}
