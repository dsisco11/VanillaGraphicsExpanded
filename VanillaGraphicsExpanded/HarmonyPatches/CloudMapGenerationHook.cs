using System;
using FluffyClouds;
using HarmonyLib;
using VanillaGraphicsExpanded.PBR.Clouds;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Observes the retained native OIT .30 callback; never suppresses or independently ticks it.</summary>
[HarmonyPatch(typeof(CloudRendererMap), nameof(CloudRendererMap.OnRenderFrame))]
internal static class CloudMapGenerationHook
{
    #region Private
    /// <summary>Expires the old map before native ticking and establishes an observed callback boundary.</summary>
    private static void Prefix(CloudRendererMap __instance, out CloudNativeMapAdapter? __state)
    {
        __state = CloudNativeRuntime.Active?.ForMap(__instance);
        __state?.BeginGeneration();
    }

    /// <summary>Publishes matching placement and content after normal generation completion.</summary>
    private static void Postfix(CloudNativeMapAdapter? __state, bool __runOriginal)
    {
        if (__state is not null && CloudNativeRuntime.Active is { } runtime && runtime.TryGetFrame(out uint frame))
            __state.CompleteGeneration(frame, __runOriginal);
    }

    /// <summary>Native exceptions preserve their original propagation while withdrawing map availability.</summary>
    private static void Finalizer(Exception? __exception, CloudNativeMapAdapter? __state)
    {
        if (__exception is not null) __state?.Publication.BeginGeneration();
    }
    #endregion
}
