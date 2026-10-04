using System;
using System.Reflection;
using HarmonyLib;
using VanillaGraphicsExpanded.PBR.SceneColor;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Brackets the original particle submission without delaying, replacing or replaying it.</summary>
[HarmonyPatch]
internal static class SceneColorParticleCaptureHook
{
    #region Public API
    /// <summary>Targets the installed model-selecting submission called after engine blend setup.</summary>
    [HarmonyTargetMethod]
    internal static MethodBase TargetMethod() => AccessTools.Method(
        "Vintagestory.Client.NoObf.SystemRenderParticles:Render", [typeof(int), typeof(float)])
        ?? throw new MissingMethodException("SystemRenderParticles.Render(int, float)");

    /// <summary>Redirects only prepared scene cube particles while preserving original arguments and execution.</summary>
    [HarmonyPrefix]
    internal static void Prefix(int __0, out SceneColorParticleDrawScope? __state)
        => __state = SceneColorParticleCapture.BeginDraw(__0);

    /// <summary>Publishes successful capture and always restores the engine draw boundary.</summary>
    [HarmonyFinalizer]
    internal static void Finalizer(Exception? __exception, SceneColorParticleDrawScope? __state)
    {
        if (__state is null) return;
        try { if (__exception is null) __state.Complete(); }
        finally { __state.Dispose(); }
    }
    #endregion
}
