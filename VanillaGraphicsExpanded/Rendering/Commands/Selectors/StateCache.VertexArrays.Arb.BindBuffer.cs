using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Arb BindBuffer dispatch and its completion bookkeeping.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload once and completes its selector or routing bookkeeping.</summary>
    internal void ExecuteArbBindBuffer(BufferTargetArb @target, int @buffer)
    {
        var observation = StateCache.BeginNativeBufferBinding((BufferTarget)@target);
        bool returned = false;
        try
        {
            GL.Arb.BindBuffer(@target, @buffer);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload once and completes its selector or routing bookkeeping.</summary>
    internal void ExecuteArbBindBuffer(BufferTargetArb @target, uint @buffer)
    {
        var observation = StateCache.BeginNativeBufferBinding((BufferTarget)@target);
        bool returned = false;
        try
        {
            GL.Arb.BindBuffer(@target, @buffer);
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
