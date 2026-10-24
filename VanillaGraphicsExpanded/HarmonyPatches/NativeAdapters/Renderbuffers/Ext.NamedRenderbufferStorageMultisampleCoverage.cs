using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Renderbuffers;

/// <summary>Forwards GL.Ext.NamedRenderbufferStorageMultisampleCoverage overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedRenderbufferStorageMultisampleCoverage(int @renderbuffer, int @coverageSamples, int @colorSamples, InternalFormat @internalformat, int @width, int @height)
    {
        var observation = StateCache.BeginNativeRenderbufferStorage(unchecked((int)@renderbuffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedRenderbufferStorageMultisampleCoverage(@renderbuffer, @coverageSamples, @colorSamples, @internalformat, @width, @height);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void NamedRenderbufferStorageMultisampleCoverage(uint @renderbuffer, int @coverageSamples, int @colorSamples, InternalFormat @internalformat, int @width, int @height)
    {
        var observation = StateCache.BeginNativeRenderbufferStorage(unchecked((int)@renderbuffer), true);
        bool returned = false;
        try
        {
            GL.Ext.NamedRenderbufferStorageMultisampleCoverage(@renderbuffer, @coverageSamples, @colorSamples, @internalformat, @width, @height);
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
