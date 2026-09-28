using System;
using HarmonyLib;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Profiling;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Scopes the engine RenderFinalComposition boundary using the shared GPU debug abstraction.</summary>
[HarmonyPatch(typeof(ClientPlatformWindows), nameof(ClientPlatformWindows.RenderFinalComposition), new Type[] {  })]
internal static class EngineFinalCompositionScopeHook
{
    #region Patch lifecycle
    /// <summary>Leaves release rendering untouched.</summary>
    [HarmonyPrepare]
    private static bool Prepare()
    {
#if DEBUG
        return true;
#else
        return false;
#endif
    }

    /// <summary>Opens the rendering boundary's debug group.</summary>
    [HarmonyPrefix]
    internal static void Prefix(out GlDebug.GroupScope __state) =>
        __state = GlDebug.Group("VS.RenderFinalComposition");

    /// <summary>Closes the group on normal and exceptional exits without suppressing errors.</summary>
    [HarmonyFinalizer]
    internal static void Finalizer(GlDebug.GroupScope __state) => __state.Dispose();
    #endregion
}
