using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.BufferStorage;

/// <summary>Forwards GL.Ext.NamedBufferStorageExternal overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorageExternal(int @buffer, IntPtr @offset, int @size, IntPtr @clientBuffer, BufferStorageMask @flags)
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferStorageExternal(@buffer, @offset, @size, @clientBuffer, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorageExternal(int @buffer, IntPtr @offset, int @size, IntPtr @clientBuffer, MapBufferUsageMask @flags)
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferStorageExternal(@buffer, @offset, @size, @clientBuffer, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorageExternal(int @buffer, IntPtr @offset, IntPtr @size, IntPtr @clientBuffer, BufferStorageMask @flags)
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferStorageExternal(@buffer, @offset, @size, @clientBuffer, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorageExternal(int @buffer, IntPtr @offset, IntPtr @size, IntPtr @clientBuffer, MapBufferUsageMask @flags)
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferStorageExternal(@buffer, @offset, @size, @clientBuffer, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorageExternal(uint @buffer, IntPtr @offset, int @size, IntPtr @clientBuffer, BufferStorageMask @flags)
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferStorageExternal(@buffer, @offset, @size, @clientBuffer, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorageExternal(uint @buffer, IntPtr @offset, int @size, IntPtr @clientBuffer, MapBufferUsageMask @flags)
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferStorageExternal(@buffer, @offset, @size, @clientBuffer, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorageExternal(uint @buffer, IntPtr @offset, IntPtr @size, IntPtr @clientBuffer, BufferStorageMask @flags)
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferStorageExternal(@buffer, @offset, @size, @clientBuffer, @flags);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedBufferStorageExternal(uint @buffer, IntPtr @offset, IntPtr @size, IntPtr @clientBuffer, MapBufferUsageMask @flags)
    {
        var observation = StateCache.BeginNativeBufferStorage(unchecked((int)@buffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedBufferStorageExternal(@buffer, @offset, @size, @clientBuffer, @flags);
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
