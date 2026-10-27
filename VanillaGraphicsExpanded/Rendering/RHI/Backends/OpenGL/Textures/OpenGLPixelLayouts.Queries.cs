using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Checks original context identity between individual pixel-layout reads.</summary>
internal static partial class OpenGLPixelLayouts
{
    #region Private
    /// <summary>Reads one unknown selector without accepting a replacement context.</summary>
    private static int ReadLayoutValue(StateCache cache, RHICommandContext backend, GetPName parameter)
    {
        backend.RequireContext();
        int result = cache.QueryBoundary(() => GL.GetInteger(parameter));
        backend.RequireContext();
        return result;
    }
    #endregion
}
