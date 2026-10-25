using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Core NamedRenderbufferStorage execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreNamedRenderbufferStorage(int @renderbuffer, RenderbufferStorage @internalformat, int @width, int @height)
    {
        var observation = StateCache.BeginNativeRenderbufferStorage(unchecked((int)@renderbuffer), true);
        bool returned = false;
        try
        {
            GL.NamedRenderbufferStorage(@renderbuffer, @internalformat, @width, @height);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreNamedRenderbufferStorage(uint @renderbuffer, RenderbufferStorage @internalformat, int @width, int @height)
    {
        var observation = StateCache.BeginNativeRenderbufferStorage(unchecked((int)@renderbuffer), true);
        bool returned = false;
        try
        {
            GL.NamedRenderbufferStorage(@renderbuffer, @internalformat, @width, @height);
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
