using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;
using VanillaGraphicsExpanded;
using VanillaGraphicsExpanded.Rendering;

/// <summary>Integrates primary attachments and GL state with engine framebuffer lifecycle boundaries.</summary>
[Harmony]
public static class GBufferHooks
{

    #region Engine framebuffer lifecycle
    /// <summary>Marks primary attachments for setup when the engine unloads the framebuffer.</summary>
    [HarmonyPatch(typeof(ClientPlatformWindows), nameof(ClientPlatformWindows.UnloadFrameBuffer), typeof(EnumFrameBuffer))]
    [HarmonyPrefix]
    public static void UnloadFrameBuffer_Hook(EnumFrameBuffer framebuffer)
    {
       GBufferManager.Instance?.UnloadGBuffer(framebuffer);
    }

    /// <summary>Refreshes dependent resources after replacement framebuffers are published and old textures deleted.</summary>
    [HarmonyPatch(typeof(ClientPlatformWindows), nameof(ClientPlatformWindows.RebuildFrameBuffers))]
    [HarmonyPostfix]
    public static void RebuildFrameBuffers_Hook()
    {
        // SetupDefaultFrameBuffers only returns a replacement list. RebuildFrameBuffers publishes
        // it and deletes the old list before this postfix may borrow textures or restore GL bindings.
        ScreenResourceManager.HandleScreenResize();
    }

    /// <summary>Clears VGE attachments alongside the primary framebuffer.</summary>
    [HarmonyPatch(typeof(ClientPlatformWindows), nameof(ClientPlatformWindows.ClearFrameBuffer), typeof(EnumFrameBuffer))]
    [HarmonyPostfix]
    public static void ClearFrameBuffer_Hook(EnumFrameBuffer framebuffer)
    {
        GBufferManager.Instance?.ClearGBuffer(framebuffer);
    }

    /// <summary>Attaches primary targets on initial load and reapplies the extended draw-buffer policy.</summary>
    [HarmonyPatch(typeof(ClientPlatformWindows), nameof(ClientPlatformWindows.LoadFrameBuffer), typeof(EnumFrameBuffer))]
    [HarmonyPostfix]
    public static void LoadFrameBuffer_Hook(EnumFrameBuffer framebuffer)
    {
        GBufferManager.Instance?.LoadGBuffer(framebuffer);
    }

    #endregion

    #region Engine blend state
    /// <summary>Restores attachment-specific blending after an engine global blend change.</summary>
    [HarmonyPatch(typeof(ClientPlatformWindows), nameof(ClientPlatformWindows.GlToggleBlend), typeof(bool), typeof(EnumBlendMode))]
    [HarmonyPostfix]
    public static void GlToggleBlend_Hook(bool on, EnumBlendMode blendMode)
    {
        GBufferManager.Instance?.ReapplyGBufferBlendState();
    }
    #endregion
}
