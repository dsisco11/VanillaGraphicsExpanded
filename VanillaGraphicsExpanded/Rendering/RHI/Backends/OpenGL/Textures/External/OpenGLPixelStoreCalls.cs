using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Preserves exact external dispatch and propagates defined pixel-store effects into existing knowledge.</summary>
internal static class OpenGLPixelStoreCalls
{
    #region Internal API
    /// <summary>Forwards an integer setter once and preserves known state for arguments the specification rejects.</summary>
    internal static void SetPixelStore(OpenGlStateCache cache, PixelStoreParameter parameter, int value)
    {
        var origin = Integration.RenderContextRegistry.CaptureResourceContext();
        // Callbacks must not observe the previous field as current while its native effect is in progress.
        if (Accepts(parameter, value)) ForgetField(cache, parameter);
        try { GL.PixelStore(parameter, value); }
        catch { if (cache.OwnsKnowledgeFor(origin)) ForgetField(cache, parameter); throw; }
        if (!Integration.RenderContextRegistry.IsCurrentResourceContext(origin))
        { if (cache.OwnsKnowledgeFor(origin)) ForgetField(cache, parameter); return; }
        if (cache.OwnsKnowledgeFor(origin) && Accepts(parameter, value)) CommitField(cache, parameter, value);
    }

    /// <summary>Forwards the original float while separately applying specified boolean or nearest-integer semantics.</summary>
    internal static void SetPixelStore(OpenGlStateCache cache, PixelStoreParameter parameter, float value)
    {
        var origin = Integration.RenderContextRegistry.CaptureResourceContext();
        bool boolean = IsBoolean(parameter);
        double rounded = Math.Round((double)value);
        bool uncertain = !boolean && (!float.IsFinite(value) || rounded < int.MinValue || rounded > int.MaxValue
            || Math.Abs((double)value - Math.Truncate(value)) == 0.5);
        int converted = boolean ? value != 0 ? 1 : 0 : uncertain ? 0 : (int)rounded;
        bool accepted = !uncertain && Accepts(parameter, converted);
        // Ties and out-of-range integer conversion have no uniquely specified resulting value.
        if (accepted || uncertain) ForgetField(cache, parameter);
        try { GL.PixelStore(parameter, value); }
        catch { if (cache.OwnsKnowledgeFor(origin)) ForgetField(cache, parameter); throw; }
        if (!Integration.RenderContextRegistry.IsCurrentResourceContext(origin))
        { if (cache.OwnsKnowledgeFor(origin)) ForgetField(cache, parameter); return; }
        if (accepted && cache.OwnsKnowledgeFor(origin)) CommitField(cache, parameter, converted);
    }
    #endregion

    #region Private
    /// <summary>Distinguishes boolean conversion from the integer conversion used by stride and alignment fields.</summary>
    private static bool IsBoolean(PixelStoreParameter parameter) => parameter is
        PixelStoreParameter.PackSwapBytes or PixelStoreParameter.PackLsbFirst
        or PixelStoreParameter.UnpackSwapBytes or PixelStoreParameter.UnpackLsbFirst;

    /// <summary>Recognizes the defined acceptance conditions for represented layout fields without draining errors.</summary>
    private static bool Accepts(PixelStoreParameter parameter, int value) => parameter switch
    {
        PixelStoreParameter.PackAlignment or PixelStoreParameter.UnpackAlignment => value is 1 or 2 or 4 or 8,
        PixelStoreParameter.PackRowLength or PixelStoreParameter.PackSkipRows or PixelStoreParameter.PackSkipPixels
            or PixelStoreParameter.PackImageHeight or PixelStoreParameter.PackSkipImages
            or PixelStoreParameter.UnpackRowLength or PixelStoreParameter.UnpackSkipRows or PixelStoreParameter.UnpackSkipPixels
            or PixelStoreParameter.UnpackImageHeight or PixelStoreParameter.UnpackSkipImages => value >= 0,
        _ => IsBoolean(parameter)
    };

    /// <summary>Updates the single existing layout authority, leaving unrelated fields untouched.</summary>
    private static void CommitField(OpenGlStateCache cache, PixelStoreParameter parameter, int value)
    {
        cache.ObservePixelPackField(parameter, value);
        cache.ObservePixelUnpackField(parameter, value);
    }

    /// <summary>Withdraws only the affected value after a throw or unspecified conversion outcome.</summary>
    private static void ForgetField(OpenGlStateCache cache, PixelStoreParameter parameter)
    {
        cache.ForgetPixelPackField(parameter);
        cache.ForgetPixelUnpackField(parameter);
    }
    #endregion
}
