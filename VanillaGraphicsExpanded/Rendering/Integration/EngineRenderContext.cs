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
        if (!TryRegisterCurrent())
            throw new InvalidOperationException("The engine render window context is unavailable or not current.");
    }

    /// <summary>Registers an available current engine window without treating ordinary startup absence as failure.</summary>
    internal static unsafe bool TryRegisterCurrent()
    {
        if (ScreenManager.Platform is not ClientPlatformWindows platform || platform.window is null)
            return false;
        var owner = platform.window;
        if (OpenTK.Windowing.GraphicsLibraryFramework.GLFW.GetCurrentContext() != owner.WindowPtr)
            return false;
        if (!owner.Exists) return false;
        RenderContextRegistry.RegisterCurrent(owner, static target => ((GameWindowNative)target).Exists);
        return true;
    }
    #endregion
}
