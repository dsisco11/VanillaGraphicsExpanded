using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Core TextureParameter execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTextureParameter(int @texture, TextureParameterName @pname, int @param)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, StateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.TextureParameter(@texture, @pname, @param);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTextureParameter(int @texture, TextureParameterName @pname, int[] @param)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, StateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.TextureParameter(@texture, @pname, @param);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTextureParameter(int @texture, TextureParameterName @pname, int* @param)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, StateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.TextureParameter(@texture, @pname, @param);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTextureParameter(int @texture, TextureParameterName @pname, float @param)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, StateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.TextureParameter(@texture, @pname, @param);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTextureParameter(int @texture, TextureParameterName @pname, float[] @param)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, StateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.TextureParameter(@texture, @pname, @param);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTextureParameter(int @texture, TextureParameterName @pname, float* @param)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, StateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.TextureParameter(@texture, @pname, @param);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTextureParameter(uint @texture, TextureParameterName @pname, int @param)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, StateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.TextureParameter(@texture, @pname, @param);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTextureParameter(uint @texture, TextureParameterName @pname, int[] @param)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, StateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.TextureParameter(@texture, @pname, @param);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTextureParameter(uint @texture, TextureParameterName @pname, int* @param)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, StateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.TextureParameter(@texture, @pname, @param);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTextureParameter(uint @texture, TextureParameterName @pname, float @param)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, StateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.TextureParameter(@texture, @pname, @param);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTextureParameter(uint @texture, TextureParameterName @pname, float[] @param)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, StateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.TextureParameter(@texture, @pname, @param);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes its affected metadata publication.</summary>
    internal void ExecuteCoreTextureParameter(uint @texture, TextureParameterName @pname, float* @param)
    {
        var observation = StateCache.BeginNativeTextureMetadata(unchecked((int)@texture), 0, -1, true, false, StateCache.TextureMetadataEffect.Parameter, unchecked((int)@pname));
        bool returned = false;
        try
        {
            GL.TextureParameter(@texture, @pname, @param);
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
