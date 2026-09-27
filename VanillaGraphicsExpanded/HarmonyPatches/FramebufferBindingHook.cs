using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Keeps VGE framebuffer tracking synchronized with the engine's raw framebuffer binds.</summary>
[HarmonyPatch]
internal static class FramebufferBindingHook
{
    #region Engine binding boundaries

    /// <summary>Observes both engine setters, which bind the combined read/draw framebuffer target.</summary>
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.PropertySetter(typeof(ClientPlatformWindows), "CurrentFrameBuffer");
        yield return AccessTools.PropertySetter(typeof(ClientPlatformWindows), "CurrentFrameBufferKeepVw");
    }

    /// <summary>Records the completed bind without another driver query or redundant GL operation.</summary>
    [HarmonyPostfix]
    internal static void Postfix(FrameBufferRef? __0)
    {
        // OIT and primary framebuffer attachments have different blend policies. Tracking
        // this boundary prevents primary-only repair from changing OIT accumulation.
        GlStateCache.Current.SetFramebufferCache(FramebufferTarget.Framebuffer, __0?.FboId ?? 0);
    }

    #endregion
}
