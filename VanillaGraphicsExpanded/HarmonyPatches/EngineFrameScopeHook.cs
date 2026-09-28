using System;
using HarmonyLib;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.Client;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Scopes engine Render work outside registered world render stages.</summary>
// ScreenManager.Render is non-public in the engine DLL; C# cannot use nameof for this member.
[HarmonyPatch(typeof(ScreenManager), "Render", new Type[] { typeof(float) })]
internal static class EngineFrameScopeHook
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
        __state = GlDebug.Group("VS." + nameof(ScreenManager) + "." + "Render");

    /// <summary>Closes the group on both normal and exceptional exits.</summary>
    [HarmonyFinalizer]
    internal static void Finalizer(GlDebug.GroupScope __state) => __state.Dispose();
    #endregion
}
