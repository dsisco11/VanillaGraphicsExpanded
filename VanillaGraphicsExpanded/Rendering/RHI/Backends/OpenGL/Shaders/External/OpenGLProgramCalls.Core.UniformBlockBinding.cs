using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Core UniformBlockBinding execution and affected metadata completion.</summary>
internal static unsafe partial class OpenGLProgramCalls
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteCoreUniformBlockBinding(int @program, int @uniformBlockIndex, int @uniformBlockBinding)
    {
        var observation = OpenGlStateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, false, -1, 1, false, false, ProgramInterface.UniformBlock, unchecked((int)@uniformBlockIndex));
        bool returned = false;
        try
        {
            GL.UniformBlockBinding(@program, @uniformBlockIndex, @uniformBlockBinding);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteCoreUniformBlockBinding(uint @program, uint @uniformBlockIndex, uint @uniformBlockBinding)
    {
        var observation = OpenGlStateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, false, -1, 1, false, false, ProgramInterface.UniformBlock, unchecked((int)@uniformBlockIndex));
        bool returned = false;
        try
        {
            GL.UniformBlockBinding(@program, @uniformBlockIndex, @uniformBlockBinding);
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
