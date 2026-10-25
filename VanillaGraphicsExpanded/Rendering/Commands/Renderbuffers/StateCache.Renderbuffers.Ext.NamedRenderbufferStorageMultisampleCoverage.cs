using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Ext NamedRenderbufferStorageMultisampleCoverage execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtNamedRenderbufferStorageMultisampleCoverage(int @renderbuffer, int @coverageSamples, int @colorSamples, InternalFormat @internalformat, int @width, int @height)
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

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteExtNamedRenderbufferStorageMultisampleCoverage(uint @renderbuffer, int @coverageSamples, int @colorSamples, InternalFormat @internalformat, int @width, int @height)
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
