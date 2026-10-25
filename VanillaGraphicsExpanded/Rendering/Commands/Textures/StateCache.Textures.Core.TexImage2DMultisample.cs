using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Core TexImage2DMultisample execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTexImage2DMultisample(TextureTargetMultisample @target, int @samples, PixelInternalFormat @internalformat, int @width, int @height, bool @fixedsamplelocations)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Storage, 0);
        bool returned = false;
        try
        {
            GL.TexImage2DMultisample(@target, @samples, @internalformat, @width, @height, @fixedsamplelocations);
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
