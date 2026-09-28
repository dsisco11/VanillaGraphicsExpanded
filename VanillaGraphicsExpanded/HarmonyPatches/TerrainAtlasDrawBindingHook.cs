using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Attaches VGE atlas resources at engine terrain atlas selection call sites, independent of setter inlining.</summary>
[HarmonyPatch]
internal static class TerrainAtlasDrawBindingHook
{
    #region Engine layout
    private static readonly MethodInfo Opaque = AccessTools.PropertySetter(typeof(ShaderProgramChunkopaque), "TerrainTex2D");
    private static readonly MethodInfo Topsoil = AccessTools.PropertySetter(typeof(ShaderProgramChunktopsoil), "TerrainTex2D");
    private static readonly MethodInfo Liquid = AccessTools.PropertySetter(typeof(ShaderProgramChunkliquid), "TerrainTex2D");
    private static readonly MethodInfo Transparent = AccessTools.PropertySetter(typeof(ShaderProgramChunktransparent), "TerrainTex2D");
    private static readonly MethodInfo Shadow = AccessTools.PropertySetter(typeof(ShaderProgramChunkshadowmap), "Tex2d2D");

    /// <summary>Enumerates each renderer entry point which selects a terrain atlas before submitting geometry.</summary>
    internal static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (string name in new[] { "RenderOpaque", "RenderShadow", "RenderOIT", "RenderAfterOIT" })
            yield return AccessTools.Method(typeof(ChunkRenderer), name)
                ?? throw new MissingMethodException(typeof(ChunkRenderer).FullName, name);
    }

    /// <summary>Defines the installed engine's exact primary atlas assignment counts for each renderer.</summary>
    private static Dictionary<MethodInfo, int> ExpectedCalls(MethodBase method) => method.Name switch
    {
        "RenderOpaque" => new() { [Opaque] = 4, [Topsoil] = 1 },
        "RenderShadow" => new() { [Shadow] = 4 },
        "RenderOIT" => new() { [Liquid] = 1, [Transparent] = 1 },
        "RenderAfterOIT" => new() { [Opaque] = 1 },
        _ => throw new InvalidOperationException($"Unsupported terrain atlas renderer: {method}.")
    };
    #endregion

    #region Call site injection
    /// <summary>Preserves original atlas setters and appends the VGE binder with their exact evaluated operands.</summary>
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase __originalMethod)
    {
        var code = instructions.ToList();
        var expected = ExpectedCalls(__originalMethod);
        var setters = new[] { Opaque, Topsoil, Liquid, Transparent, Shadow };
        foreach (var setter in setters)
        {
            int count = code.Count(instruction => instruction.Calls(setter));
            int wanted = expected.GetValueOrDefault(setter);
            if (count != wanted)
                throw new InvalidOperationException($"VGE terrain atlas binding: {__originalMethod.Name} has {count} calls to {setter.DeclaringType?.Name}.{setter.Name}; expected {wanted}. Engine layout changed.");
        }
        var bind = AccessTools.Method(typeof(TerrainAtlasDrawBindingHook), nameof(BindAtlas));
        var atlas = generator.DeclareLocal(typeof(int));
        // Store the concrete receiver type, so replaying the original call remains verifiable IL.
        var receivers = expected.Keys.ToDictionary(setter => setter, setter => generator.DeclareLocal(setter.DeclaringType!));
        for (int index = 0; index < code.Count; index++)
        {
            var instruction = code[index];
            if (instruction.operand is not MethodInfo setter || !expected.ContainsKey(setter) || !instruction.Calls(setter))
            {
                yield return instruction;
                continue;
            }
            if (index > 0 && code[index - 1].opcode.OpCodeType == OpCodeType.Prefix)
                throw new InvalidOperationException($"VGE terrain atlas binding cannot rewrite prefixed call in {__originalMethod.Name}.");

            var saveAtlas = new CodeInstruction(OpCodes.Stloc, atlas);
            // Incoming branches and exception-region starts must still execute the operand capture.
            saveAtlas.labels.AddRange(instruction.labels);
            saveAtlas.blocks.AddRange(instruction.blocks.Where(block => block.blockType != ExceptionBlockType.EndExceptionBlock));
            yield return saveAtlas;
            yield return new CodeInstruction(OpCodes.Stloc, receivers[setter]);
            yield return new CodeInstruction(OpCodes.Ldloc, receivers[setter]);
            yield return new CodeInstruction(OpCodes.Ldloc, atlas);
            yield return new CodeInstruction(instruction.opcode, setter);
            yield return new CodeInstruction(OpCodes.Ldloc, receivers[setter]);
            yield return new CodeInstruction(OpCodes.Ldloc, atlas);
            var callback = new CodeInstruction(OpCodes.Call, bind);
            callback.blocks.AddRange(instruction.blocks.Where(block => block.blockType == ExceptionBlockType.EndExceptionBlock));
            yield return callback;
        }
    }

    /// <summary>Binds material, normal, relief and Surface Cache atlas resources after the engine atlas assignment.</summary>
    internal static void BindAtlas(ShaderProgramBase program, int atlasId)
    {
        TerrainMaterialParamsTextureBindingHook.BindAtlas(program, atlasId);
        TerrainLumonSceneChunkSlotUniformBindingHook.Use_Postfix(program);
    }
    #endregion
}

