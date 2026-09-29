using System;
using HarmonyLib;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Lets the engine own compilation and recovers a failed patched program from retained source.</summary>
[HarmonyPatch(typeof(ShaderProgram), nameof(ShaderProgram.Compile))]
internal static class ShaderPatchCompilationHook
{
    #region Compilation result
    /// <summary>Retries after all compilation hooks finish; successful fallback consumes the original failure.</summary>
    [HarmonyFinalizer]
    internal static Exception? Finalizer(ShaderProgram __instance, ref bool __result, Exception? __exception)
    {
        if (__exception is null && __result)
        {
            ShaderCapabilities.PublishDeclared(__instance);
            return null;
        }
        ShaderCapabilities.InvalidateLinked(__instance);
        try
        {
            if (ShaderPatchRecovery.TryRecover(__instance, __instance.Compile,
                detail => EngineShaderProcessingHook.ReportFailure(__instance.PassName,
                    __exception is null ? detail : detail + "\n" + __exception), out bool recovered))
            {
                __result = recovered;
                if (recovered) return null;
            }
        }
        catch (Exception recoveryError)
        {
            EngineShaderProcessingHook.ReportFailure(__instance.PassName, recoveryError.ToString());
            return __exception ?? recoveryError;
        }
        return __exception;
    }
    #endregion
}
