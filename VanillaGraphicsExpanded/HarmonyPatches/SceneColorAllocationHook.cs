using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.SceneColor;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Changes scene storage at the engine's original allocation calls, preserving publication and retirement.</summary>
[HarmonyPatch(typeof(ClientPlatformWindows), nameof(ClientPlatformWindows.SetupDefaultFrameBuffers))]
internal static class SceneColorAllocationHook
{
    [ThreadStatic] private static SceneColorAllocation allocation;

    #region Public API
    /// <summary>Restores allocation context even if an engine rebuild fails or nests another setup.</summary>
    [HarmonyPrefix]
    internal static void Prefix(out SceneColorAllocation __state)
    {
        __state = allocation;
        allocation = default;
        allocation.Begin(EnumFrameBuffer.Default);
    }

    /// <summary>Leaves exceptions with the engine while restoring the caller's allocation sequence.</summary>
    [HarmonyFinalizer]
    internal static void Finalizer(SceneColorAllocation __state) => allocation = __state;

    /// <summary>Retains the engine list assignment and records which framebuffer the following allocations populate.</summary>
    internal static void Assign(List<FrameBufferRef> targets, int index, FrameBufferRef target)
    {
        targets[index] = target;
        allocation.Begin((EnumFrameBuffer)index);
    }

    /// <summary>Supplies the selected format at the original allocation site without reallocating texture names.</summary>
    internal static PixelInternalFormat SelectFormat(PixelInternalFormat format) => allocation.Select(format);

    /// <summary>Wraps framebuffer assignments and RGBA8 format operands while retaining labels and exception regions.</summary>
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> source)
    {
        var instructions = source.Select(instruction => new CodeInstruction(instruction)).ToList();
        var setter = AccessTools.PropertySetter(typeof(List<FrameBufferRef>), "Item");
        var assign = AccessTools.Method(typeof(SceneColorAllocationHook), nameof(Assign));
        var select = AccessTools.Method(typeof(SceneColorAllocationHook), nameof(SelectFormat));
        // Refuse missing allocation boundaries before installing edits. Installed-source
        // validation additionally checks primary color/glow ordering and both SSAO branches.
        if (!instructions.Any(instruction => instruction.Calls(setter))
            || !instructions.Any(IsRgba8))
            throw new InvalidOperationException("The engine framebuffer allocation layout is unsupported for scene HDR.");

        foreach (var instruction in instructions)
        {
            if (instruction.Calls(setter))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = assign;
            }
            yield return instruction;
            if (IsRgba8(instruction)) yield return new CodeInstruction(OpCodes.Call, select);
        }
    }
    #endregion

    #region Private
    /// <summary>Recognizes the sized RGBA8 format constant without changing other allocation operands.</summary>
    private static bool IsRgba8(CodeInstruction instruction) => instruction.opcode == OpCodes.Ldc_I4
        && instruction.operand is int value && value == (int)PixelInternalFormat.Rgba8;
    #endregion
}
