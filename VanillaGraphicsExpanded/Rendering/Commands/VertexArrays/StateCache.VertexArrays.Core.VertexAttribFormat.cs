using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Core VertexAttribFormat dispatch and its vertex revision publication.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteCoreVertexAttribFormat(int @attribindex, int @size, VertexAttribType @type, bool @normalized, int @relativeoffset)
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribFormat(@attribindex, @size, @type, @normalized, @relativeoffset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteCoreVertexAttribFormat(uint @attribindex, int @size, VertexAttribType @type, bool @normalized, uint @relativeoffset)
    {
        var observation = StateCache.BeginNativeVertexMetadata(0, false, false);
        bool returned = false;
        try
        {
            GL.VertexAttribFormat(@attribindex, @size, @type, @normalized, @relativeoffset);
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
