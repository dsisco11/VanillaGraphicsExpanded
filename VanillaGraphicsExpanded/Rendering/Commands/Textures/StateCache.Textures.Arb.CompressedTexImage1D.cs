using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Arb CompressedTexImage1D execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteArbCompressedTexImage1D(TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, int @imageSize, IntPtr @data)
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Arb.CompressedTexImage1D(@target, @level, @internalformat, @width, @border, @imageSize, @data);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteArbCompressedTexImage1D<T6>(TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, int @imageSize, ref T6 @data)
        where T6 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Arb.CompressedTexImage1D<T6>(@target, @level, @internalformat, @width, @border, @imageSize, ref @data);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteArbCompressedTexImage1D<T6>(TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, int @imageSize, T6[,,] @data)
        where T6 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Arb.CompressedTexImage1D<T6>(@target, @level, @internalformat, @width, @border, @imageSize, @data);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteArbCompressedTexImage1D<T6>(TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, int @imageSize, T6[,] @data)
        where T6 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Arb.CompressedTexImage1D<T6>(@target, @level, @internalformat, @width, @border, @imageSize, @data);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteArbCompressedTexImage1D<T6>(TextureTarget @target, int @level, InternalFormat @internalformat, int @width, int @border, int @imageSize, T6[] @data)
        where T6 : struct
    {
        var observation = StateCache.BeginNativeTextureMetadata(0, unchecked((int)@target), -1, false, false, StateCache.TextureMetadataEffect.Storage, 0, unchecked((int)@level));
        bool returned = false;
        try
        {
            GL.Arb.CompressedTexImage1D<T6>(@target, @level, @internalformat, @width, @border, @imageSize, @data);
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
