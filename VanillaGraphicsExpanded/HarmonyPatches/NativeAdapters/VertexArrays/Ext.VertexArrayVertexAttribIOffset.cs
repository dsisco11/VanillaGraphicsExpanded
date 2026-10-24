using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.Ext.VertexArrayVertexAttribIOffset overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexArrayVertexAttribIOffset(int @vaobj, int @buffer, int @index, int @size, VertexAttribEnum @type, int @stride, IntPtr @offset)
    {
        var observation = StateCache.BeginNativeVertexMetadata(unchecked((int)@vaobj), true, false);
        bool returned = false;
        try
        {
            GL.Ext.VertexArrayVertexAttribIOffset(@vaobj, @buffer, @index, @size, @type, @stride, @offset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexArrayVertexAttribIOffset(int @vaobj, int @buffer, int @index, int @size, VertexAttribType @type, int @stride, IntPtr @offset)
    {
        var observation = StateCache.BeginNativeVertexMetadata(unchecked((int)@vaobj), true, false);
        bool returned = false;
        try
        {
            GL.Ext.VertexArrayVertexAttribIOffset(@vaobj, @buffer, @index, @size, @type, @stride, @offset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexArrayVertexAttribIOffset(uint @vaobj, uint @buffer, uint @index, int @size, VertexAttribEnum @type, int @stride, IntPtr @offset)
    {
        var observation = StateCache.BeginNativeVertexMetadata(unchecked((int)@vaobj), true, false);
        bool returned = false;
        try
        {
            GL.Ext.VertexArrayVertexAttribIOffset(@vaobj, @buffer, @index, @size, @type, @stride, @offset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexArrayVertexAttribIOffset(uint @vaobj, uint @buffer, uint @index, int @size, VertexAttribType @type, int @stride, IntPtr @offset)
    {
        var observation = StateCache.BeginNativeVertexMetadata(unchecked((int)@vaobj), true, false);
        bool returned = false;
        try
        {
            GL.Ext.VertexArrayVertexAttribIOffset(@vaobj, @buffer, @index, @size, @type, @stride, @offset);
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
