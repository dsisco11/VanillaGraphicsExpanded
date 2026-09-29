using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;
using VanillaGraphicsExpanded;
using VanillaGraphicsExpanded.Rendering;

[Harmony]
public static class GBufferHooks
{

// Vintagestory.Client.NoObf.ClientPlatformWindows.UnloadFrameBuffer(EnumFrameBuffer framebuffer)
    [HarmonyPatch(typeof(ClientPlatformWindows), nameof(ClientPlatformWindows.UnloadFrameBuffer), typeof(EnumFrameBuffer))]
    [HarmonyPrefix]
    public static void UnloadFrameBuffer_Hook(EnumFrameBuffer framebuffer)
    {
       GBufferManager.Instance?.UnloadGBuffer(framebuffer);
    }

    // Initial setup happens before mods load, but later calls rebuild these objects after resize.
    [HarmonyPatch(typeof(ClientPlatformWindows), nameof(ClientPlatformWindows.SetupDefaultFrameBuffers))]
    [HarmonyPostfix]
    public static void SetupDefaultFrameBuffers_Hook()
    {
        ScreenResourceManager.HandleScreenResize();
    }

// Vintagestory.Client.NoObf.ClientPlatformWindows.ClearFrameBuffer(EnumFrameBuffer framebuffer)
    [HarmonyPatch(typeof(ClientPlatformWindows), nameof(ClientPlatformWindows.ClearFrameBuffer), typeof(EnumFrameBuffer))]
    [HarmonyPostfix]
    public static void ClearFrameBuffer_Hook(EnumFrameBuffer framebuffer)
    {
        GBufferManager.Instance?.ClearGBuffer(framebuffer);
    }

// Vintagestory.Client.NoObf.ClientPlatformWindows.LoadFrameBuffer(EnumFrameBuffer framebuffer)
    [HarmonyPatch(typeof(ClientPlatformWindows), nameof(ClientPlatformWindows.LoadFrameBuffer), typeof(EnumFrameBuffer))]
    [HarmonyPostfix]
    public static void LoadFrameBuffer_Hook(EnumFrameBuffer framebuffer)
    {
        GBufferManager.Instance?.LoadGBuffer(framebuffer);
    }

// Vintagestory.Client.NoObf.ClientPlatformWindows.GlToggleBlend(bool on, EnumBlendMode blendMode)
    [HarmonyPatch(typeof(ClientPlatformWindows), nameof(ClientPlatformWindows.GlToggleBlend), typeof(bool), typeof(EnumBlendMode))]
    [HarmonyPostfix]
    public static void GlToggleBlend_Hook(bool on, EnumBlendMode blendMode)
    {
        GBufferManager.Instance?.ReapplyGBufferBlendState();
    }
}