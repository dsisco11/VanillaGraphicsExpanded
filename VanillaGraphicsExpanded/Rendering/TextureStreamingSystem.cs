using System;

namespace VanillaGraphicsExpanded.Rendering;

internal static class TextureStreamingSystem
{
    private static readonly object Gate = new();
    private static TextureStreamingManager? manager;

    public static TextureStreamingManager Manager
    {
        get
        {
            lock (Gate)
            {
                manager ??= new TextureStreamingManager();
                return manager;
            }
        }
    }

    public static void Configure(TextureStreamingSettings settings)
    {
        Manager.UpdateSettings(settings);
    }

    public static TextureStreamingDiagnostics GetDiagnosticsSnapshot()
    {
        return Manager.GetDiagnosticsSnapshot();
    }

    public static void TickOnRenderThread()
    {
        Manager.TickOnRenderThread();
    }

    public static void Dispose()
    {
        lock (Gate)
        {
            manager?.Dispose();
            manager = null;
        }
    }
}
