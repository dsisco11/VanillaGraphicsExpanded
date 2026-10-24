using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.BufferStorage;

/// <summary>Forwards GL.Ext.NamedBufferStorageMem overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorageMem(int @buffer, int @size, int @memory, long @offset)
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferStorageMem(@buffer, @size, @memory, @offset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorageMem(int @buffer, IntPtr @size, int @memory, long @offset)
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferStorageMem(@buffer, @size, @memory, @offset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorageMem(uint @buffer, int @size, uint @memory, ulong @offset)
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferStorageMem(@buffer, @size, @memory, @offset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorageMem(uint @buffer, IntPtr @size, uint @memory, ulong @offset)
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferStorageMem(@buffer, @size, @memory, @offset);
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
