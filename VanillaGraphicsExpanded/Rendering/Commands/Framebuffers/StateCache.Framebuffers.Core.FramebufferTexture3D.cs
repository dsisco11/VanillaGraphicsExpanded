using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Core FramebufferTexture3D execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal void ExecuteCoreFramebufferTexture3D(FramebufferTarget @target, FramebufferAttachment @attachment, TextureTarget @textarget, int @texture, int @level, int @zoffset)
    {
        var observation = StateCache.BeginNativeFramebufferAttachment(unchecked((int)@target), false, unchecked((int)@attachment), unchecked((int)@texture), unchecked((int)@level), -1, 3);
        bool returned = false;
        try
        {
            GL.FramebufferTexture3D(@target, @attachment, @textarget, @texture, @level, @zoffset);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal void ExecuteCoreFramebufferTexture3D(FramebufferTarget @target, FramebufferAttachment @attachment, TextureTarget @textarget, uint @texture, int @level, int @zoffset)
    {
        var observation = StateCache.BeginNativeFramebufferAttachment(unchecked((int)@target), false, unchecked((int)@attachment), unchecked((int)@texture), unchecked((int)@level), -1, 3);
        bool returned = false;
        try
        {
            GL.FramebufferTexture3D(@target, @attachment, @textarget, @texture, @level, @zoffset);
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
