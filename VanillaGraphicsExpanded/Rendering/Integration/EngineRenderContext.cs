using System;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;
namespace VanillaGraphicsExpanded.Rendering.Integration;
/// <summary>Connects renderer initialization to the engine's actual native-window lifetime.</summary>
internal static class EngineRenderContext
{
    #region Public API
    /// <summary>Registers the current engine window; world leave and shader reload retain its generation.</summary>
    internal static unsafe void RegisterCurrent()
    {
        if (ScreenManager.Platform is not ClientPlatformWindows platform || platform.window is null)
            throw new InvalidOperationException("The engine render window is unavailable.");
        var owner = platform.window;
        if (OpenTK.Windowing.GraphicsLibraryFramework.GLFW.GetCurrentContext() != owner.WindowPtr)
            throw new InvalidOperationException("The engine window context is not current.");
        RenderContextRegistry.RegisterCurrent(owner, static target => ((GameWindowNative)target).Exists);
    }
    #endregion
}
