using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Retains the existing per-name outcome policy for exact external deletion.</summary>
internal static partial class OpenGLBufferDeletionCalls
{
    #region Internal API
    /// <summary>Checks deletion only, without draining native errors or treating a list as atomic.</summary>
    internal static bool WasDeleted(int id) => !GL.IsBuffer(id);
    #endregion
}
