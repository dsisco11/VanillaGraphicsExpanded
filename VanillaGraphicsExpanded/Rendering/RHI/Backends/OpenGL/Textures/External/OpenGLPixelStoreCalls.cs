using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Preserves exact external dispatch and propagates defined pixel-store effects into existing knowledge.</summary>
internal static class OpenGLPixelStoreCalls
{
    #region Internal API

    #endregion

    #region Private
    /// <summary>Distinguishes boolean conversion from the integer conversion used by stride and alignment fields.</summary>
    internal static bool IsBoolean(PixelStoreParameter parameter) => parameter is
        PixelStoreParameter.PackSwapBytes or PixelStoreParameter.PackLsbFirst
        or PixelStoreParameter.UnpackSwapBytes or PixelStoreParameter.UnpackLsbFirst;

    /// <summary>Recognizes the defined acceptance conditions for represented layout fields without draining errors.</summary>
    internal static bool Accepts(PixelStoreParameter parameter, int value) => parameter switch
    {
        PixelStoreParameter.PackAlignment or PixelStoreParameter.UnpackAlignment => value is 1 or 2 or 4 or 8,
        PixelStoreParameter.PackRowLength or PixelStoreParameter.PackSkipRows or PixelStoreParameter.PackSkipPixels
            or PixelStoreParameter.PackImageHeight or PixelStoreParameter.PackSkipImages
            or PixelStoreParameter.UnpackRowLength or PixelStoreParameter.UnpackSkipRows or PixelStoreParameter.UnpackSkipPixels
            or PixelStoreParameter.UnpackImageHeight or PixelStoreParameter.UnpackSkipImages => value >= 0,
        _ => IsBoolean(parameter)
    };

    /// <summary>Updates the single existing layout authority, leaving unrelated fields untouched.</summary>
    internal static void CommitField(OpenGlStateCache cache, PixelStoreParameter parameter, int value)
    {
        cache.ObservePixelPackField(parameter, value);
        cache.ObservePixelUnpackField(parameter, value);
    }

    /// <summary>Withdraws only the affected value after a throw or unspecified conversion outcome.</summary>
    internal static void ForgetField(OpenGlStateCache cache, PixelStoreParameter parameter)
    {
        cache.ForgetPixelPackField(parameter);
        cache.ForgetPixelUnpackField(parameter);
    }
    #endregion
}
