using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Ext NamedRenderbufferStorageMultisampleCoverage execution and affected metadata completion.</summary>
internal static unsafe partial class OpenGLRenderbuffersCalls
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedRenderbufferStorageMultisampleCoverage(int @renderbuffer, int @coverageSamples, int @colorSamples, InternalFormat @internalformat, int @width, int @height)
    {
        var observation = OpenGlStateCache.BeginNativeRenderbufferStorage(unchecked((int)@renderbuffer), true);
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

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal static void ExecuteExtNamedRenderbufferStorageMultisampleCoverage(uint @renderbuffer, int @coverageSamples, int @colorSamples, InternalFormat @internalformat, int @width, int @height)
    {
        var observation = OpenGlStateCache.BeginNativeRenderbufferStorage(unchecked((int)@renderbuffer), true);
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
