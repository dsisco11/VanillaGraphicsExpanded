using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Arb UseProgramObject dispatch and its completion bookkeeping.</summary>
internal static unsafe partial class OpenGLProgramCalls
{
    #region Internal API
    /// <summary>Executes the exact external overload once and completes its selector or routing bookkeeping.</summary>
    internal static void ExecuteArbUseProgramObject(int @programObj)
    {
        var observation = OpenGlStateCache.BeginNativeProgramSelection(false);
        bool returned = false;
        try
        {
            GL.Arb.UseProgramObject(@programObj);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload once and completes its selector or routing bookkeeping.</summary>
    internal static void ExecuteArbUseProgramObject(uint @programObj)
    {
        var observation = OpenGlStateCache.BeginNativeProgramSelection(false);
        bool returned = false;
        try
        {
            GL.Arb.UseProgramObject(@programObj);
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
