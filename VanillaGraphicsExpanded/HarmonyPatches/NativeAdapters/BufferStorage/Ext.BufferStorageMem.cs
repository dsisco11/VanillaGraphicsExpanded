using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.BufferStorage;

/// <summary>Forwards GL.Ext.BufferStorageMem overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void BufferStorageMem(BufferTargetArb @target, int @size, int @memory, long @offset)
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@target), false);
        bool returned = false;
        try
        {
            GL.Ext.BufferStorageMem(@target, @size, @memory, @offset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void BufferStorageMem(BufferTargetArb @target, int @size, uint @memory, ulong @offset)
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@target), false);
        bool returned = false;
        try
        {
            GL.Ext.BufferStorageMem(@target, @size, @memory, @offset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void BufferStorageMem(BufferTargetArb @target, IntPtr @size, int @memory, long @offset)
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@target), false);
        bool returned = false;
        try
        {
            GL.Ext.BufferStorageMem(@target, @size, @memory, @offset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void BufferStorageMem(BufferTargetArb @target, IntPtr @size, uint @memory, ulong @offset)
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@target), false);
        bool returned = false;
        try
        {
            GL.Ext.BufferStorageMem(@target, @size, @memory, @offset);
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
