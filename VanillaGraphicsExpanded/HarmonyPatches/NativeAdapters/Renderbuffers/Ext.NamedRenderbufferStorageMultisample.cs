using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Renderbuffers;

/// <summary>Forwards GL.Ext.NamedRenderbufferStorageMultisample overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedRenderbufferStorageMultisample(int @renderbuffer, int @samples, ExtDirectStateAccess @internalformat, int @width, int @height)
    {
        var observation = StateCache.BeginNativeRenderbufferStorage(unchecked((int)@renderbuffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedRenderbufferStorageMultisample(@renderbuffer, @samples, @internalformat, @width, @height);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedRenderbufferStorageMultisample(int @renderbuffer, int @samples, PixelInternalFormat @internalformat, int @width, int @height)
    {
        var observation = StateCache.BeginNativeRenderbufferStorage(unchecked((int)@renderbuffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedRenderbufferStorageMultisample(@renderbuffer, @samples, @internalformat, @width, @height);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedRenderbufferStorageMultisample(int @renderbuffer, int @samples, RenderbufferStorage @internalformat, int @width, int @height)
    {
        var observation = StateCache.BeginNativeRenderbufferStorage(unchecked((int)@renderbuffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedRenderbufferStorageMultisample(@renderbuffer, @samples, @internalformat, @width, @height);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedRenderbufferStorageMultisample(uint @renderbuffer, int @samples, ExtDirectStateAccess @internalformat, int @width, int @height)
    {
        var observation = StateCache.BeginNativeRenderbufferStorage(unchecked((int)@renderbuffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedRenderbufferStorageMultisample(@renderbuffer, @samples, @internalformat, @width, @height);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedRenderbufferStorageMultisample(uint @renderbuffer, int @samples, PixelInternalFormat @internalformat, int @width, int @height)
    {
        var observation = StateCache.BeginNativeRenderbufferStorage(unchecked((int)@renderbuffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedRenderbufferStorageMultisample(@renderbuffer, @samples, @internalformat, @width, @height);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedRenderbufferStorageMultisample(uint @renderbuffer, int @samples, RenderbufferStorage @internalformat, int @width, int @height)
    {
        var observation = StateCache.BeginNativeRenderbufferStorage(unchecked((int)@renderbuffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedRenderbufferStorageMultisample(@renderbuffer, @samples, @internalformat, @width, @height);
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
