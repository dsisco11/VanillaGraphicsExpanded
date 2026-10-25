using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Core VertexArrayAttribLFormat dispatch and its vertex revision publication.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteCoreVertexArrayAttribLFormat(int @vaobj, int @attribindex, int @size, VertexAttribDoubleType @type, int @relativeoffset)
    {
        var observation = StateCache.BeginNativeVertexMetadata(unchecked((int)@vaobj), true, false);
        bool returned = false;
        try
        {
            GL.VertexArrayAttribLFormat(@vaobj, @attribindex, @size, @type, @relativeoffset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteCoreVertexArrayAttribLFormat(int @vaobj, int @attribindex, int @size, VertexAttribLType @type, int @relativeoffset)
    {
        var observation = StateCache.BeginNativeVertexMetadata(unchecked((int)@vaobj), true, false);
        bool returned = false;
        try
        {
            GL.VertexArrayAttribLFormat(@vaobj, @attribindex, @size, @type, @relativeoffset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteCoreVertexArrayAttribLFormat(int @vaobj, int @attribindex, int @size, VertexAttribType @type, int @relativeoffset)
    {
        var observation = StateCache.BeginNativeVertexMetadata(unchecked((int)@vaobj), true, false);
        bool returned = false;
        try
        {
            GL.VertexArrayAttribLFormat(@vaobj, @attribindex, @size, @type, @relativeoffset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteCoreVertexArrayAttribLFormat(uint @vaobj, uint @attribindex, int @size, VertexAttribDoubleType @type, uint @relativeoffset)
    {
        var observation = StateCache.BeginNativeVertexMetadata(unchecked((int)@vaobj), true, false);
        bool returned = false;
        try
        {
            GL.VertexArrayAttribLFormat(@vaobj, @attribindex, @size, @type, @relativeoffset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteCoreVertexArrayAttribLFormat(uint @vaobj, uint @attribindex, int @size, VertexAttribLType @type, uint @relativeoffset)
    {
        var observation = StateCache.BeginNativeVertexMetadata(unchecked((int)@vaobj), true, false);
        bool returned = false;
        try
        {
            GL.VertexArrayAttribLFormat(@vaobj, @attribindex, @size, @type, @relativeoffset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact overload and completes its vertex metadata operation.</summary>
    internal void ExecuteCoreVertexArrayAttribLFormat(uint @vaobj, uint @attribindex, int @size, VertexAttribType @type, uint @relativeoffset)
    {
        var observation = StateCache.BeginNativeVertexMetadata(unchecked((int)@vaobj), true, false);
        bool returned = false;
        try
        {
            GL.VertexArrayAttribLFormat(@vaobj, @attribindex, @size, @type, @relativeoffset);
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
