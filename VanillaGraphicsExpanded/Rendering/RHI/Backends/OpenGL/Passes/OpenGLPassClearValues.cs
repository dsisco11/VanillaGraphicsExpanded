using System;
using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes typed clear payloads without converting integer values through floating-point storage.</summary>
internal static class OpenGLPassClearValues
{
    #region Internal API
    /// <summary>Dispatches the represented scalar class after pass validation and clear-only state setup.</summary>
    internal static unsafe void ClearColor(int output, ColorClearValue value)
    {
        if (!value.FloatComponents.IsEmpty)
        {
            fixed (float* data = value.FloatComponents) GL.ClearBuffer(ClearBuffer.Color, output, data);
        }
        else if (!value.SignedComponents.IsEmpty)
        {
            fixed (int* data = value.SignedComponents) GL.ClearBuffer(ClearBuffer.Color, output, data);
        }
        else
        {
            fixed (uint* data = value.UnsignedComponents) GL.ClearBuffer(ClearBuffer.Color, output, data);
        }
    }
    #endregion
}
