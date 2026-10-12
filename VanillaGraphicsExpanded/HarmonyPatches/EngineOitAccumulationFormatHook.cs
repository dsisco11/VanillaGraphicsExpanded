using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Widens the engine-owned transparent buckets at their original allocation boundary.</summary>
[HarmonyPatch(typeof(SystemRenderOITLayers.BeforeOIT), "rebuild")]
internal static class EngineOitAccumulationFormatHook
{
    #region Public API
    /// <summary>Changes only bucket storage, preserving texture names, attachments and engine retirement.</summary>
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> source)
    {
        var instructions = source.Select(instruction => new CodeInstruction(instruction)).ToList();
        var allocations = instructions.Where(instruction => instruction.operand is MethodInfo method
            && method.DeclaringType == typeof(GL) && method.Name == nameof(GL.TexImage3D)).ToList();
        var formats = instructions.Select((instruction, index) => (instruction, index))
            .Where(entry => IsConstant(entry.instruction, (int)PixelInternalFormat.Rgba16f)).ToList();

        // The installed rebuild allocates one array, with target and mip immediately before
        // its internal format. Reject changed layouts rather than widening unrelated storage.
        if (allocations.Count != 1 || formats.Count != 1 || formats[0].index < 2
            || !IsConstant(instructions[formats[0].index - 2], (int)TextureTarget.Texture2DArray)
            || !IsConstant(instructions[formats[0].index - 1], 0)
            || formats[0].index >= instructions.IndexOf(allocations[0]))
            throw new InvalidOperationException("The engine OIT bucket allocation layout is unsupported.");

        // Editing the existing operand retains branch labels and exception regions. Allocation,
        // three-layer publication, sampling, blending and disposal stay with the engine.
        formats[0].instruction.operand = (int)PixelInternalFormat.Rgba32f;
        return instructions;
    }
    #endregion

    #region Private
    /// <summary>Recognizes the integer operands used by the installed texture allocation sequence.</summary>
    private static bool IsConstant(CodeInstruction instruction, int value)
    {
        return instruction.opcode == OpCodes.Ldc_I4 && instruction.operand is int operand && operand == value
            || value == 0 && instruction.opcode == OpCodes.Ldc_I4_0;
    }
    #endregion
}
