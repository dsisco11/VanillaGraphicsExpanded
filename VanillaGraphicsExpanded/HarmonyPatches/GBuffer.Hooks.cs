using HarmonyLib;

[Harmony]
public static class GBufferHooks
{

// Vintagestory.Client.NoObf.ClientPlatformWindows.UnloadFrameBuffer(EnumFrameBuffer framebuffer)
    [HarmonyPatch(typeof(Vintagestory.Client.NoObf.ClientPlatformWindows), "UnloadFrameBuffer")]
    [HarmonyPrefix]
    public static void UnloadFrameBuffer_Hook(EnumFrameBuffer framebuffer)
    {
        GBufferManager.Instance?.UnloadGBuffer(framebuffer);
    }

// Vintagestory.Client.NoObf.ClientPlatformWindows.SetupDefaultFrameBuffers()
    [HarmonyPatch(typeof(Vintagestory.Client.NoObf.ClientPlatformWindows), "SetupDefaultFrameBuffers")]
    [HarmonyPostfix]
    public static void SetupDefaultFrameBuffers_Hook()
    {
        GBufferManager.Instance?.SetupGBuffers();
    }

// Vintagestory.Client.NoObf.ClientPlatformWindows.ClearFrameBuffer(EnumFrameBuffer framebuffer)
    [HarmonyPatch(typeof(Vintagestory.Client.NoObf.ClientPlatformWindows), "ClearFrameBuffer")]
    [HarmonyPrefix]
    public static void ClearFrameBuffer_Hook(EnumFrameBuffer framebuffer)
    {
        GBufferManager.Instance?.ClearGBuffer(framebuffer);
    }

// Vintagestory.Client.NoObf.ClientPlatformWindows.LoadFrameBuffer(EnumFrameBuffer framebuffer)
    [HarmonyPatch(typeof(Vintagestory.Client.NoObf.ClientPlatformWindows), "LoadFrameBuffer")]
    [HarmonyPrefix]
    public static void LoadFrameBuffer_Hook(EnumFrameBuffer framebuffer)
    {
        GBufferManager.Instance?.LoadGBuffer(framebuffer);
    }    
}