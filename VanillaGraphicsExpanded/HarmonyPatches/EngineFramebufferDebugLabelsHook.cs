using System.Collections.Generic;
using HarmonyLib;
using VanillaGraphicsExpanded.Rendering.Diagnostics;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Labels completed framebuffer allocations, including replacement tables after resize.</summary>
[HarmonyPatch]
internal static class EngineFramebufferDebugLabelsHook
{
    #region Public API
    /// <summary>Installs resource instrumentation only in Debug builds, matching GlDebug labeling.</summary>
    [HarmonyPrepare]
    internal static bool Prepare()
    {
#if DEBUG
        return true;
#else
        return false;
#endif
    }

    /// <summary>Labels returned allocations without participating in framebuffer resize ownership.</summary>
    [HarmonyPatch(typeof(ClientPlatformWindows), nameof(ClientPlatformWindows.SetupDefaultFrameBuffers))]
    [HarmonyPostfix]
    internal static void Defaults(List<FrameBufferRef> __result)
    {
        EngineFramebufferDebugLabels.ApplyDefaults(__result);
    }

    /// <summary>Uses the engine's authored name when a custom framebuffer has one.</summary>
    [HarmonyPatch(typeof(ClientPlatformWindows), nameof(ClientPlatformWindows.CreateFramebuffer))]
    [HarmonyPostfix]
    internal static void Custom(FrameBufferRef __result)
    {
        if (__result is not null)
            EngineFramebufferDebugLabels.Apply(__result, $"VS.{__result.FbAttrs?.Name ?? $"Framebuffer{__result.FboId}"}");
    }
    #endregion
}
