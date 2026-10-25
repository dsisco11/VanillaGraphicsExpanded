using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Arb FramebufferTextureFace execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal void ExecuteArbFramebufferTextureFace(FramebufferTarget @target, FramebufferAttachment @attachment, int @texture, int @level, TextureTarget @face)
    {
        var observation = StateCache.BeginNativeFramebufferAttachment(unchecked((int)@target), false, unchecked((int)@attachment), unchecked((int)@texture), unchecked((int)@level), -1, 3);
        bool returned = false;
        try
        {
            GL.Arb.FramebufferTextureFace(@target, @attachment, @texture, @level, @face);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal void ExecuteArbFramebufferTextureFace(FramebufferTarget @target, FramebufferAttachment @attachment, uint @texture, int @level, TextureTarget @face)
    {
        var observation = StateCache.BeginNativeFramebufferAttachment(unchecked((int)@target), false, unchecked((int)@attachment), unchecked((int)@texture), unchecked((int)@level), -1, 3);
        bool returned = false;
        try
        {
            GL.Arb.FramebufferTextureFace(@target, @attachment, @texture, @level, @face);
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
