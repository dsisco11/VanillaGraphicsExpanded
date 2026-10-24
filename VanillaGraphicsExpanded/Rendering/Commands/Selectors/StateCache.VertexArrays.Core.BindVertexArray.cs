using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Core BindVertexArray dispatch and its completion bookkeeping.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload once and completes its selector or routing bookkeeping.</summary>
    internal void ExecuteCoreBindVertexArray(int @array)
    {
        var observation = StateCache.BeginNativeVertexSelection();
        bool returned = false;
        try
        {
            GL.BindVertexArray(@array);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload once and completes its selector or routing bookkeeping.</summary>
    internal void ExecuteCoreBindVertexArray(uint @array)
    {
        var observation = StateCache.BeginNativeVertexSelection();
        bool returned = false;
        try
        {
            GL.BindVertexArray(@array);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }
    #endregion
}
