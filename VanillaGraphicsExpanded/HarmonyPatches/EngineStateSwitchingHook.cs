using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Routes supported base-game state switches through VGE's state cache at their call sites.</summary>
[HarmonyPatch]
internal static class EngineStateSwitchingHook
{
    #region Public API
    /// <summary>Discovers actual native calls, including private helpers, overloads and constructors.</summary>
    internal static IEnumerable<MethodBase> TargetMethods()
    {
        // Original assembly metadata makes selection independent of other installed transpilers.
        foreach (var method in EngineStateTargets.Discover(EngineStateTargets.Assemblies().ToArray()))
            yield return method;
    }

    /// <summary>Replaces only exact static calls, retaining evaluated operands, labels and exception blocks.</summary>
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            // Identical signatures require no argument replay, stack shuffling or extra GL operations.
            var rewritten = new CodeInstruction(instruction);
            if (instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo native
                && EngineStateCallMap.TryGetReplacement(native, out var replacement))
                rewritten.operand = replacement;
            yield return rewritten;
        }
    }
    #endregion

}
