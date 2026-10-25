using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Core TexBuffer execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTexBuffer(TextureBufferTarget @target, SizedInternalFormat @internalformat, int @buffer)
    {
        var observation = StateCache.BeginNativeBufferTexture(0, unchecked((int)@target), 0, false, false, unchecked((int)@buffer), unchecked((int)@internalformat), 0, 0, true);
        bool returned = false;
        try
        {
            GL.TexBuffer(@target, @internalformat, @buffer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTexBuffer(TextureBufferTarget @target, SizedInternalFormat @internalformat, uint @buffer)
    {
        var observation = StateCache.BeginNativeBufferTexture(0, unchecked((int)@target), 0, false, false, unchecked((int)@buffer), unchecked((int)@internalformat), 0, 0, true);
        bool returned = false;
        try
        {
            GL.TexBuffer(@target, @internalformat, @buffer);
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
