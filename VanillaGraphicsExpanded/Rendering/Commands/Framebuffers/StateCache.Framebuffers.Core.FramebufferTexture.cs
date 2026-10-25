using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Core FramebufferTexture execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal void ExecuteCoreFramebufferTexture(FramebufferTarget @target, FramebufferAttachment @attachment, int @texture, int @level)
    {
        var observation = StateCache.BeginNativeFramebufferAttachment(unchecked((int)@target), false, unchecked((int)@attachment), unchecked((int)@texture), unchecked((int)@level), -1, 0);
        bool returned = false;
        try
        {
            GL.FramebufferTexture(@target, @attachment, @texture, @level);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal void ExecuteCoreFramebufferTexture(FramebufferTarget @target, FramebufferAttachment @attachment, uint @texture, int @level)
    {
        var observation = StateCache.BeginNativeFramebufferAttachment(unchecked((int)@target), false, unchecked((int)@attachment), unchecked((int)@texture), unchecked((int)@level), -1, 0);
        bool returned = false;
        try
        {
            GL.FramebufferTexture(@target, @attachment, @texture, @level);
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
