using System;
using HarmonyLib;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.Client;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Scopes engine RenderBg work outside registered world render stages.</summary>
[HarmonyPatch(typeof(GuiCompositeMainMenuLeft), nameof(GuiCompositeMainMenuLeft.RenderBg), new Type[] { typeof(float), typeof(bool) })]
internal static class EngineMenuBackgroundScopeHook
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

    /// <summary>Opens the engine rendering boundary.</summary>
    [HarmonyPrefix]
    internal static void Prefix(out GlDebug.GroupScope __state) =>
        __state = GlDebug.Group("VS." + nameof(GuiCompositeMainMenuLeft) + "." + nameof(GuiCompositeMainMenuLeft.RenderBg));

    /// <summary>Closes the group on both normal and exceptional exits.</summary>
    [HarmonyFinalizer]
    internal static void Finalizer(GlDebug.GroupScope __state) => __state.Dispose();
    #endregion
}
