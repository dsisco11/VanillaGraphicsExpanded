using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using VanillaGraphicsExpanded.PBR.SceneColor;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Binds linear sampled inputs only at the engine's scene postprocessing call sites.</summary>
[HarmonyPatch]
internal static class SceneColorPostprocessBindingHook
{
    #region Public API
    /// <summary>Selects the retained final display endpoint; scene postprocessing is independently owned.</summary>
    internal static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(ClientPlatformWindows), nameof(ClientPlatformWindows.RenderFinalComposition));
    }

    /// <summary>Preserves engine draw order and uses the existing program activation before assigning its input convention.</summary>
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var use = AccessTools.Method(typeof(ShaderProgramBase), nameof(ShaderProgramBase.Use));
        var replacement = AccessTools.Method(typeof(SceneColorProgramBindings), nameof(SceneColorProgramBindings.UsePostprocess));
        var bloomSetter = AccessTools.PropertySetter(typeof(ShaderProgramFinal), nameof(ShaderProgramFinal.BloomParts2D));
        var raysSetter = AccessTools.PropertySetter(typeof(ShaderProgramFinal), nameof(ShaderProgramFinal.GodrayParts2D));
        bool replaced = false;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(use))
            {
                replaced = true;
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
            }
            if (instruction.Calls(bloomSetter) || instruction.Calls(raysSetter))
            {
                bool bloom = instruction.Calls(bloomSetter);
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(PBR.Postprocessing.PostprocessPipeline),
                    bloom ? nameof(PBR.Postprocessing.PostprocessPipeline.BindBloom) : nameof(PBR.Postprocessing.PostprocessPipeline.BindGodRays));
            }
            yield return instruction;
        }
        if (!replaced) throw new InvalidOperationException("Scene postprocess shader activation boundary was not found.");
    }

    /// <summary>Prevents subsequent UI or menu composition from inheriting a completed scene.</summary>
    [HarmonyFinalizer]
    internal static void Finalizer(MethodBase __originalMethod, Exception? __exception = null)
    {
        try { PBR.CameraExposure.CameraExposureDisplayBindings.EndBinding(); }
        finally
        {
            // A failed intermediate still leaves HDR scene input pending. Final consumption
            // ends the frame even if restoration itself reports an invalid borrowed resource.
            if (__originalMethod.Name == nameof(ClientPlatformWindows.RenderFinalComposition)) SceneColorPipeline.EndScene();
        }
    }
    #endregion
}
