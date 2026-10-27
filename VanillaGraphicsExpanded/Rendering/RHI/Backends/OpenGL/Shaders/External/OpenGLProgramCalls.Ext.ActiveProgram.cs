using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Ext ActiveProgram dispatch and its completion bookkeeping.</summary>
internal static unsafe partial class OpenGLProgramCalls
{
    #region Internal API
    /// <summary>Executes the exact external overload once and completes its selector or routing bookkeeping.</summary>
    internal static void ExecuteExtActiveProgram(int @program)
    {
        var observation = OpenGlStateCache.BeginNativeProgramSelection(false);
        bool returned = false;
        try
        {
            GL.Ext.ActiveProgram(@program);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload once and completes its selector or routing bookkeeping.</summary>
    internal static void ExecuteExtActiveProgram(uint @program)
    {
        var observation = OpenGlStateCache.BeginNativeProgramSelection(false);
        bool returned = false;
        try
        {
            GL.Ext.ActiveProgram(@program);
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
