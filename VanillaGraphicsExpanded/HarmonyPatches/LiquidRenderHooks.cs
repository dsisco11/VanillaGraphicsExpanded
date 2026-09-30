using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using VanillaGraphicsExpanded.PBR.Liquids;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Skips only vanilla liquid setup/submission after the owned renderer has taken responsibility.</summary>
[HarmonyPatch(typeof(ChunkRenderer), "RenderOIT")]
internal static class LiquidRenderHooks
{
    #region Engine submission boundary
    /// <summary>Guards the liquid block while preserving both matrix scopes and transparent/meta rendering.</summary>
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        var code = instructions.ToList();
        var liquid = AccessTools.Field(typeof(ShaderPrograms), nameof(ShaderPrograms.Chunkliquid));
        var transparent = AccessTools.Field(typeof(ShaderPrograms), nameof(ShaderPrograms.Chunktransparent));
        var stop = AccessTools.Method(typeof(ShaderProgramBase), nameof(ShaderProgramBase.Stop));
        int start = code.FindIndex(i => i.LoadsField(liquid));
        int next = code.FindIndex(i => i.LoadsField(transparent));
        if (start < 0 || next <= start || code.Count(i => i.LoadsField(liquid)) != 1)
            throw new InvalidOperationException("Liquid renderer boundary changed in ChunkRenderer.RenderOIT.");
        var stops = Enumerable.Range(start, next - start).Where(i => code[i].Calls(stop)).ToArray();
        if (stops.Length != 1 || code.Skip(start).Take(stops[0] - start + 1).Any(i => i.blocks.Count != 0))
            throw new InvalidOperationException("Liquid shader stop/exception boundary changed in ChunkRenderer.RenderOIT.");
        int end = stops[0] + 1;
        var resume = generator.DefineLabel();
        code[end].labels.Add(resume);
        var capture = new CodeInstruction(OpCodes.Ldarg_0);
        capture.labels.AddRange(code[start].labels);
        code[start].labels.Clear();
        code.InsertRange(start, [capture,
            new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(LiquidRenderer), nameof(LiquidRenderer.ConsumeEngineSuppression))),
            new CodeInstruction(OpCodes.Brtrue, resume)]);
        return code;
    }
    #endregion
}
