using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Selects lighting ownership each time an engine program is bound, including reused held-item programs.</summary>
[HarmonyPatch(typeof(ShaderProgramBase), nameof(ShaderProgramBase.Use))]
internal static class PbrDrawRouteHook
{
    internal static ICoreClientAPI? Api { get; set; }

    #region Binding
    /// <summary>Uses the engine's cached uniform interface; GUI and shadow draws retain their original shading.</summary>
    [HarmonyPostfix]
    internal static void Postfix(ShaderProgramBase __instance)
    {
        if (Api is null || !__instance.HasUniform("vge_pbrRoute")) return;
        var render = Api.Render;
        var buffers = render.FrameBuffers;
        __instance.Uniform("vge_pbrRoute", Route(render.CurrentRenderStage, render.CurrentFrameBuffer,
            buffers.Count > (int)EnumFrameBuffer.Primary ? buffers[(int)EnumFrameBuffer.Primary] : null,
            buffers.Count > (int)EnumFrameBuffer.Transparent ? buffers[(int)EnumFrameBuffer.Transparent] : null));
    }

    /// <summary>Opaque draws publish material data; later scene draws must produce their own lit color.</summary>
    internal static int Route(EnumRenderStage stage, FrameBufferRef? current, FrameBufferRef? primary, FrameBufferRef? transparent)
    {
        // Offscreen item/atlas draws can occur during a scene callback. The engine tracks the
        // actual target, so reject those without querying GL or guessing from the shader name.
        FrameBufferRef? expected = stage switch
        {
            EnumRenderStage.Opaque or EnumRenderStage.AfterOIT => primary,
            EnumRenderStage.OIT => transparent,
            _ => null
        };
        if (current is null || expected is null || current.FboId != expected.FboId) return 0;
        return stage == EnumRenderStage.Opaque ? 1 : 2;
    }
    #endregion
}
