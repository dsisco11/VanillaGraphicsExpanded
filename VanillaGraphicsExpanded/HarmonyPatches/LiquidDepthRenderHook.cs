using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using VanillaGraphicsExpanded.PBR.Liquids;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Hands the installed engine's liquid-depth draw block to the owned SPIR-V pass.</summary>
[HarmonyPatch(typeof(ChunkRenderer), nameof(ChunkRenderer.OnRenderBefore))]
internal static class LiquidDepthRenderHook
{
    #region Engine submission boundary
    /// <summary>Retains engine framebuffer, matrix and profiler operations around the replaced draw.</summary>
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        var code = instructions.ToList();
        var liquid = AccessTools.Field(typeof(ShaderPrograms), nameof(ShaderPrograms.Chunkliquiddepth));
        var stop = AccessTools.Method(typeof(ShaderProgramBase), nameof(ShaderProgramBase.Stop));
        int start = code.FindIndex(i => i.LoadsField(liquid));
        if (start < 0 || code.Count(i => i.LoadsField(liquid)) != 1)
            throw new InvalidOperationException("Liquid depth draw boundary changed in ChunkRenderer.OnRenderBefore.");
        int end = Enumerable.Range(start, code.Count - start).FirstOrDefault(i => code[i].Calls(stop), -1);
        if (end < 0 || end + 1 >= code.Count)
            throw new InvalidOperationException("Liquid depth shader stop boundary changed in ChunkRenderer.OnRenderBefore.");
        var resume = generator.DefineLabel();
        code[end + 1].labels.Add(resume);
        var capture = new CodeInstruction(OpCodes.Ldarg_0);
        capture.labels.AddRange(code[start].labels);
        code[start].labels.Clear();
        code.InsertRange(start, [capture,
            new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(LiquidDepthRenderer), nameof(LiquidDepthRenderer.TryRender))),
            new CodeInstruction(OpCodes.Brtrue, resume)]);
        return code;
    }
    #endregion
}
