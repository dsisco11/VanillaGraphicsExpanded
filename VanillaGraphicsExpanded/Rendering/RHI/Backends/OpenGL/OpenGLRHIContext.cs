using System;
namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Composes the admitted backend and exposes only restricted native integration operations.</summary>
internal static partial class OpenGLRHIContext
{
    [ThreadStatic] private static OpenGLBackend? backend;
    [ThreadStatic] private static RHICommandContext? context;
    #region Internal API
    /// <summary>Gets the context admitted for the current native namespace and thread.</summary>
    internal static RHICommandContext Current
    {
        get
        {
            if (backend is null || !backend.IsCurrentContext)
            {
                backend = new OpenGLBackend(StateCache.Current);
                context = new(backend);
            }
            backend.RequireContext();
            return context!;
        }
    }
    #endregion
}
