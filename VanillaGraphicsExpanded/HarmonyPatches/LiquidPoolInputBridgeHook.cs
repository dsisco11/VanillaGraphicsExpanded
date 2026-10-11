using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using VanillaGraphicsExpanded.PBR.Liquids;
using Vintagestory.API.Client;
namespace VanillaGraphicsExpanded.HarmonyPatches;
/// <summary>Redirects only verified pool-input getter calls while preserving engine traversal and finally regions.</summary>
internal static class LiquidPoolInputBridgeHook
{
    #region Internal API
    /// <summary>Rebuilds traversal after the pool draw hook is installed so native draw bodies cannot be inlined ahead of interception.</summary>
    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(MeshDataPoolManager), nameof(MeshDataPoolManager.Render)),
            transpiler: new HarmonyMethod(typeof(LiquidPoolInputBridgeHook), nameof(Transpiler)));
    }
    /// <summary>Returns the scoped liquid input translator or the genuine native shader outside submission.</summary>
    internal static IShaderProgram ReadInputs(IRenderAPI render) => LiquidGraphicsSubmission.ActiveInputs ?? render.CurrentActiveShader;
    /// <summary>Replaces the installed method's five getter calls without rebuilding branch or exception structure.</summary>
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var body = instructions.ToArray();
        var getter = AccessTools.PropertyGetter(typeof(IRenderAPI), nameof(IRenderAPI.CurrentActiveShader));
        var replacement = AccessTools.Method(typeof(LiquidPoolInputBridgeHook), nameof(ReadInputs));
        var calls = body.Where(instruction => instruction.Calls(getter)).ToArray();
        if (calls.Length != 5 || calls.Any(instruction => instruction.opcode != OpCodes.Callvirt))
            throw new InvalidOperationException("MeshDataPoolManager.Render pool-input getter shape changed; expected five virtual getter calls.");
        // The static helper consumes the same receiver argument. Keep labels and exception blocks on each original instruction.
        foreach (var instruction in calls) { instruction.opcode = OpCodes.Call; instruction.operand = replacement; }
        return body;
    }
    #endregion
}
