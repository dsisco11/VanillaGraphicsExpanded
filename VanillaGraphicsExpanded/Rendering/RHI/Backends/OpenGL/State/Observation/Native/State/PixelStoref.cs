using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes the observed effects of glPixelStoref after native dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glPixelStoref")]
    internal static void ObservePixelStoref(int a0, float a1)
    {
        PixelStoreParameter parameter = unchecked((PixelStoreParameter)a0);
        float value = unchecked((float)a1);
        var cache = OpenGLObservationContext.CurrentCache;

                var origin = Integration.RenderContextRegistry.CaptureResourceContext();
                bool boolean = OpenGLPixelStoreCalls.IsBoolean(parameter);
                double rounded = Math.Round((double)value);
                bool uncertain = !boolean && (!float.IsFinite(value) || rounded < int.MinValue || rounded > int.MaxValue
                    || Math.Abs((double)value - Math.Truncate(value)) == 0.5);
                int converted = boolean ? value != 0 ? 1 : 0 : uncertain ? 0 : (int)rounded;
                bool accepted = !uncertain && OpenGLPixelStoreCalls.Accepts(parameter, converted);
                // Ties and out-of-range integer conversion have no uniquely specified resulting value.
                if (accepted || uncertain) OpenGLPixelStoreCalls.ForgetField(cache, parameter);

                if (!Integration.RenderContextRegistry.IsCurrentResourceContext(origin))
                { if (cache.OwnsKnowledgeFor(origin)) OpenGLPixelStoreCalls.ForgetField(cache, parameter); return; }
                if (accepted && cache.OwnsKnowledgeFor(origin)) OpenGLPixelStoreCalls.CommitField(cache, parameter, converted);
    }
    #endregion
}
