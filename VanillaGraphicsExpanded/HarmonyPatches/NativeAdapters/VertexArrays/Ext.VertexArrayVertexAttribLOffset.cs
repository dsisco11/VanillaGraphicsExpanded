using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.VertexArrays;

/// <summary>Forwards GL.Ext.VertexArrayVertexAttribLOffset overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexArrayVertexAttribLOffset(int @vaobj, int @buffer, int @index, int @size, ExtDirectStateAccess @type, int @stride, IntPtr @offset)
    {
        var observation = StateCache.BeginNativeVertexMetadata(unchecked((int)@vaobj), true, false);
        bool returned = false;
        try
        {
            GL.Ext.VertexArrayVertexAttribLOffset(@vaobj, @buffer, @index, @size, @type, @stride, @offset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexArrayVertexAttribLOffset(int @vaobj, int @buffer, int @index, int @size, ExtVertexAttrib64bit @type, int @stride, IntPtr @offset)
    {
        var observation = StateCache.BeginNativeVertexMetadata(unchecked((int)@vaobj), true, false);
        bool returned = false;
        try
        {
            GL.Ext.VertexArrayVertexAttribLOffset(@vaobj, @buffer, @index, @size, @type, @stride, @offset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexArrayVertexAttribLOffset(int @vaobj, int @buffer, int @index, int @size, VertexAttribLType @type, int @stride, IntPtr @offset)
    {
        var observation = StateCache.BeginNativeVertexMetadata(unchecked((int)@vaobj), true, false);
        bool returned = false;
        try
        {
            GL.Ext.VertexArrayVertexAttribLOffset(@vaobj, @buffer, @index, @size, @type, @stride, @offset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexArrayVertexAttribLOffset(uint @vaobj, uint @buffer, uint @index, int @size, ExtDirectStateAccess @type, int @stride, IntPtr @offset)
    {
        var observation = StateCache.BeginNativeVertexMetadata(unchecked((int)@vaobj), true, false);
        bool returned = false;
        try
        {
            GL.Ext.VertexArrayVertexAttribLOffset(@vaobj, @buffer, @index, @size, @type, @stride, @offset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexArrayVertexAttribLOffset(uint @vaobj, uint @buffer, uint @index, int @size, ExtVertexAttrib64bit @type, int @stride, IntPtr @offset)
    {
        var observation = StateCache.BeginNativeVertexMetadata(unchecked((int)@vaobj), true, false);
        bool returned = false;
        try
        {
            GL.Ext.VertexArrayVertexAttribLOffset(@vaobj, @buffer, @index, @size, @type, @stride, @offset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void VertexArrayVertexAttribLOffset(uint @vaobj, uint @buffer, uint @index, int @size, VertexAttribLType @type, int @stride, IntPtr @offset)
    {
        var observation = StateCache.BeginNativeVertexMetadata(unchecked((int)@vaobj), true, false);
        bool returned = false;
        try
        {
            GL.Ext.VertexArrayVertexAttribLOffset(@vaobj, @buffer, @index, @size, @type, @stride, @offset);
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
