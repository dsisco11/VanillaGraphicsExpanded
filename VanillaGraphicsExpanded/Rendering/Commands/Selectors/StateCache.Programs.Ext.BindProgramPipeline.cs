using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Ext BindProgramPipeline dispatch and its completion bookkeeping.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload once and completes its selector or routing bookkeeping.</summary>
    internal void ExecuteExtBindProgramPipeline(int @pipeline)
    {
        var observation = StateCache.BeginNativeProgramSelection(true);
        bool returned = false;
        try
        {
            GL.Ext.BindProgramPipeline(@pipeline);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload once and completes its selector or routing bookkeeping.</summary>
    internal void ExecuteExtBindProgramPipeline(uint @pipeline)
    {
        var observation = StateCache.BeginNativeProgramSelection(true);
        bool returned = false;
        try
        {
            GL.Ext.BindProgramPipeline(@pipeline);
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
