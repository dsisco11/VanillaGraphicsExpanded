using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Tessellation;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Carries the engine's pass selection into terrain policy without a reverse membership index.</summary>
[HarmonyPatch]
internal static class TerrainPassDrawHook
{
    [ThreadStatic] private static EnumChunkRenderPass? currentPass;
    #region Public API
    /// <summary>Targets the terrain paths which share opaque and shadow executables.</summary>
    internal static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(ChunkRenderer), nameof(ChunkRenderer.RenderOpaque));
        yield return AccessTools.Method(typeof(ChunkRenderer), nameof(ChunkRenderer.RenderShadow));
    }

    /// <summary>Captures the evaluated pass index at each direct engine selection, independent of pass order or count.</summary>
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        var code = instructions.ToList();
        var table = AccessTools.Field(typeof(ChunkRenderer), "poolsByRenderPass");
        var render = AccessTools.Method(typeof(MeshDataPoolManager), nameof(MeshDataPoolManager.Render));
        var dispatch = AccessTools.Method(typeof(TerrainPassDrawHook), nameof(Render));
        var pass = generator.DeclareLocal(typeof(EnumChunkRenderPass));
        bool pending = false;
        int captures = 0;
        for (int i = 0; i < code.Count; i++)
        {
            var instruction = code[i];
            // A branch into or out of a pending selection could pair a draw with another pass.
            // Accept only a straight-line selection-to-draw interval; never guess across control flow.
            if (pending && (instruction.labels.Count != 0 || instruction.blocks.Count != 0
                || instruction.opcode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch or FlowControl.Return or FlowControl.Throw))
                throw new InvalidOperationException("Terrain pass selection crosses ambiguous control flow.");
            if (instruction.LoadsField(table))
            {
                if (pending) throw new InvalidOperationException("Terrain draw has overlapping pool selections.");
                yield return instruction;
                // These are array-index operands, not particular pass values or atlas-local numbers.
                // Nops may occur in Debug IL without changing the stack contract.
                foreach (var expected in new[] { true, false, true, false })
                {
                    while (++i < code.Count && code[i].opcode == OpCodes.Nop)
                    {
                        if (code[i].labels.Count != 0 || code[i].blocks.Count != 0)
                            throw new InvalidOperationException("Terrain selection has an alternate entry.");
                        yield return code[i];
                    }
                    if (i >= code.Count || code[i].labels.Count != 0 || code[i].blocks.Count != 0
                        || (expected ? !IsIndexLoad(code[i]) : code[i].opcode != OpCodes.Ldelem_Ref))
                        throw new InvalidOperationException("Terrain manager is not selected directly by pass and atlas.");
                    if (!expected && !pending)
                    {
                        // Stack is [table, evaluated pass]. Copy the index without reevaluating it.
                        yield return new CodeInstruction(OpCodes.Dup);
                        yield return new CodeInstruction(OpCodes.Stloc, pass);
                        pending = true;
                    }
                    yield return code[i];
                }
                captures++;
                continue;
            }
            if (!instruction.Calls(render))
            {
                // Between selection and draw only argument loads are supported. Stack rewrites,
                // calls or prefixes could replace the receiver or separate it from its category.
                if (pending && !IsIndexLoad(instruction) && instruction.opcode != OpCodes.Ldstr
                    && instruction.opcode != OpCodes.Ldnull && instruction.opcode != OpCodes.Nop)
                    throw new InvalidOperationException("Terrain manager receiver is not preserved directly to its draw.");
                yield return instruction;
                continue;
            }
            if (!pending || instruction.opcode != OpCodes.Callvirt)
                throw new InvalidOperationException("Terrain draw lacks an unambiguous pass selection.");
            yield return new CodeInstruction(OpCodes.Ldloc, pass);
            yield return new CodeInstruction(OpCodes.Call, dispatch);
            pending = false;
        }
        if (pending || captures == 0)
            throw new InvalidOperationException("Terrain pool selections are missing or have no corresponding draw.");
    }
    /// <summary>Scopes policy around the original engine draw, preserving culling, arguments and exceptional unwind.</summary>
    internal static void Render(MeshDataPoolManager manager, Vec3d position, string origin, EnumFrustumCullMode culling, EnumChunkRenderPass pass)
    {
        bool previous = TerrainDisplacementRuntime.EligiblePool;
        var previousPass = currentPass;
        try
        {
            currentPass = pass;
            TerrainDisplacementRuntime.EligiblePool = TerrainDisplacementRuntime.IsEligiblePass(pass);
            TerrainSurfaceNormals.Bind(pass);
            manager.Render(position, origin, culling);
        }
        finally
        {
            TerrainDisplacementRuntime.EligiblePool = previous;
            currentPass = previousPass;
            if (previousPass.HasValue) TerrainSurfaceNormals.Bind(previousPass.Value);
        }
    }
    #endregion

    #region Private
    /// <summary>Recognizes scalar index operands without depending on their value or local/argument slot.</summary>
    private static bool IsIndexLoad(CodeInstruction instruction) => instruction.IsLdloc() || instruction.IsLdarg()
        || instruction.opcode == OpCodes.Ldc_I4 || instruction.opcode == OpCodes.Ldc_I4_S
        || instruction.opcode.Value >= OpCodes.Ldc_I4_M1.Value && instruction.opcode.Value <= OpCodes.Ldc_I4_8.Value;
    #endregion
}
