using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Core UniformBlockBinding execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal void ExecuteCoreUniformBlockBinding(int @program, int @uniformBlockIndex, int @uniformBlockBinding)
    {
        var observation = StateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, false, -1, 1, false, false, ProgramInterface.UniformBlock, unchecked((int)@uniformBlockIndex));
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
    internal void ExecuteCoreUniformBlockBinding(uint @program, uint @uniformBlockIndex, uint @uniformBlockBinding)
    {
        var observation = StateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, false, -1, 1, false, false, ProgramInterface.UniformBlock, unchecked((int)@uniformBlockIndex));
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
