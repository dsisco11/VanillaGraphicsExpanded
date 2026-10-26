using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Preserves exact external pixel-store conversion and invalidates affected layout knowledge.</summary>
internal static class OpenGLPixelStoreCalls
{
    #region Internal API
    /// <summary>Forwards one integer pixel-store change after withdrawing the affected layout knowledge.</summary>
    internal static void SetPixelStore(StateCache cache, PixelStoreParameter parameter, int value)
    {
        InvalidatePixelTransferLayout(cache, parameter);
        GL.PixelStore(parameter, value);
    }

    /// <summary>Preserves native float-to-integer conversion without retaining stale layout knowledge.</summary>
    internal static void SetPixelStore(StateCache cache, PixelStoreParameter parameter, float value)
    {
        InvalidatePixelTransferLayout(cache, parameter);
        GL.PixelStore(parameter, value);
    }

    /// <summary>Forgets only layout fields that the specified native pixel-store command can change.</summary>
    private static void InvalidatePixelTransferLayout(StateCache cache, PixelStoreParameter parameter)
    {
        // Pack owns the readback layout; unpack additionally owns image strides for layered uploads.
        if (parameter is PixelStoreParameter.PackAlignment or PixelStoreParameter.PackRowLength
            or PixelStoreParameter.PackSkipRows or PixelStoreParameter.PackSkipPixels
            or PixelStoreParameter.PackSwapBytes or PixelStoreParameter.PackLsbFirst or PixelStoreParameter.PackImageHeight or PixelStoreParameter.PackSkipImages) cache.DirtyPixelPackState();
        else if (parameter is PixelStoreParameter.UnpackAlignment or PixelStoreParameter.UnpackRowLength
            or PixelStoreParameter.UnpackSkipRows or PixelStoreParameter.UnpackSkipPixels
            or PixelStoreParameter.UnpackSwapBytes or PixelStoreParameter.UnpackLsbFirst
            or PixelStoreParameter.UnpackImageHeight or PixelStoreParameter.UnpackSkipImages) cache.DirtyPixelUnpackState();
    }
    #endregion
}
