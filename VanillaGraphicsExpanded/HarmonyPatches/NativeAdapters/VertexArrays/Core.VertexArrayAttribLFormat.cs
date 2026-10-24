using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.VertexArrayAttribLFormat overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexArrayAttribLFormat(int @vaobj, int @attribindex, int @size, VertexAttribDoubleType @type, int @relativeoffset)
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

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexArrayAttribLFormat(int @vaobj, int @attribindex, int @size, VertexAttribLType @type, int @relativeoffset)
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

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexArrayAttribLFormat(int @vaobj, int @attribindex, int @size, VertexAttribType @type, int @relativeoffset)
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

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexArrayAttribLFormat(uint @vaobj, uint @attribindex, int @size, VertexAttribDoubleType @type, uint @relativeoffset)
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

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexArrayAttribLFormat(uint @vaobj, uint @attribindex, int @size, VertexAttribLType @type, uint @relativeoffset)
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

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexArrayAttribLFormat(uint @vaobj, uint @attribindex, int @size, VertexAttribType @type, uint @relativeoffset)
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
