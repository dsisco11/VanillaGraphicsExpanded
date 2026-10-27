using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Core BindProgramPipeline dispatch and its completion bookkeeping.</summary>
internal static unsafe partial class OpenGLProgramCalls
{
    #region Internal API
    /// <summary>Executes the exact external overload once and completes its selector or routing bookkeeping.</summary>
    internal static void ExecuteCoreBindProgramPipeline(int @pipeline)
    {
        var observation = OpenGlStateCache.BeginNativeProgramSelection(true);
        bool returned = false;
        try
        {
            GL.BindProgramPipeline(@pipeline);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload once and completes its selector or routing bookkeeping.</summary>
    internal static void ExecuteCoreBindProgramPipeline(uint @pipeline)
    {
        var observation = OpenGlStateCache.BeginNativeProgramSelection(true);
        bool returned = false;
        try
        {
            GL.BindProgramPipeline(@pipeline);
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
