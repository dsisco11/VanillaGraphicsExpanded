using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Ext UseShaderProgram dispatch and its completion bookkeeping.</summary>
internal static unsafe partial class OpenGLProgramCalls
{
    #region Internal API
    /// <summary>Executes the exact external overload once and completes its selector or routing bookkeeping.</summary>
    internal static void ExecuteExtUseShaderProgram(ExtSeparateShaderObjects @type, int @program)
    {
        var observation = StateCache.BeginNativeProgramSelection(false);
        bool returned = false;
        try
        {
            GL.Ext.UseShaderProgram(@type, @program);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload once and completes its selector or routing bookkeeping.</summary>
    internal static void ExecuteExtUseShaderProgram(ExtSeparateShaderObjects @type, uint @program)
    {
        var observation = StateCache.BeginNativeProgramSelection(false);
        bool returned = false;
        try
        {
            GL.Ext.UseShaderProgram(@type, @program);
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
