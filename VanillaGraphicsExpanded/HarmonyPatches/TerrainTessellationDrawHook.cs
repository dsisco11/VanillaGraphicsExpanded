using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using VanillaGraphicsExpanded.PBR.Tessellation;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Matches grouped terrain and ordinary shared-shadow submissions to the installed executable's topology.</summary>
[HarmonyPatch]
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
    /// <summary>Includes ordinary meshes because entity shadows reuse the terrain shadow executable.</summary>
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(ClientPlatformWindows), nameof(ClientPlatformWindows.RenderMesh),
            [typeof(MeshRef)]);
        yield return AccessTools.Method(typeof(ClientPlatformWindows), nameof(ClientPlatformWindows.RenderMesh),
            [typeof(MeshRef), typeof(int[]), typeof(int[]), typeof(int), typeof(bool)]);
    }

    /// <summary>Validates each installed engine draw layout before replacing its topology operands.</summary>
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase? __originalMethod = null)
    {
        var code = instructions.ToList();
        var field = AccessTools.Field(typeof(VAO), nameof(VAO.drawMode));
        bool ordinary = __originalMethod?.GetParameters().Length == 1;
        bool supported = code.Count(i => i.opcode == OpCodes.Ldfld && Equals(i.operand, field)) == (ordinary ? 1 : 2);
        if (ordinary) TerrainTessellationPrograms.MeshDrawHookAvailable = supported;
        else TerrainTessellationPrograms.DrawHookAvailable = supported;
        if (!supported)
        {
            TerrainTessellationPrograms.Log?.Invoke("[VGE] Terrain tessellation disabled: unsupported mesh draw layout.");
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
