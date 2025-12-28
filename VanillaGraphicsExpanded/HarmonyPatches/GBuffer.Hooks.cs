using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;
using VanillaGraphicsExpanded;

[Harmony]
public static class GBufferHooks
{

// Vintagestory.Client.NoObf.ClientPlatformWindows.UnloadFrameBuffer(EnumFrameBuffer framebuffer)
    [HarmonyPatch(typeof(ClientPlatformWindows), nameof(ClientPlatformWindows.UnloadFrameBuffer), typeof(EnumFrameBuffer))]
    [HarmonyPostfix]
    public static void UnloadFrameBuffer_Hook(EnumFrameBuffer framebuffer)
    {
       GBufferManager.Instance?.UnloadGBuffer(framebuffer);
    }

// Vintagestory.Client.NoObf.ClientPlatformWindows.SetupDefaultFrameBuffers()
    [HarmonyPatch(typeof(ClientPlatformWindows), nameof(ClientPlatformWindows.SetupDefaultFrameBuffers))]
    [HarmonyPostfix]
    public static void SetupDefaultFrameBuffers_Hook()
    {
        GBufferManager.Instance?.SetupGBuffers();
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
}