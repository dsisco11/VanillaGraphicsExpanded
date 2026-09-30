using HarmonyLib;
using VanillaGraphicsExpanded.Rendering.Diagnostics;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Labels bucket resources when the engine extends its initial transparent framebuffer.</summary>
[HarmonyPatch(typeof(SystemRenderOITLayers.BeforeOIT), "rebuild")]
internal static class EngineOitDebugLabelsHook
{
    private static readonly AccessTools.FieldRef<int> Reveal = AccessTools.StaticFieldRefAccess<int>(AccessTools.Field(typeof(SystemRenderOITLayers), "revealTextureId"));
    private static readonly AccessTools.FieldRef<int> Accumulation = AccessTools.StaticFieldRefAccess<int>(AccessTools.Field(typeof(SystemRenderOITLayers), "accumTextureId"));

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

    /// <summary>Uses cached field accessors because the bucket IDs are not published in FrameBufferRef.</summary>
    [HarmonyPostfix]
    internal static void Postfix(FrameBufferRef ___transparentfb)
    {
        EngineFramebufferDebugLabels.ApplyOit(___transparentfb, Reveal(), Accumulation());
    }
    #endregion
}
