using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Core ShaderStorageBlockBinding execution and affected metadata completion.</summary>
internal static unsafe partial class OpenGLProgramCalls
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteCoreShaderStorageBlockBinding(int @program, int @storageBlockIndex, int @storageBlockBinding)
    {
        var observation = StateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, false, -1, 1, false, false, ProgramInterface.ShaderStorageBlock, unchecked((int)@storageBlockIndex));
        bool returned = false;
        try
        {
            GL.ShaderStorageBlockBinding(@program, @storageBlockIndex, @storageBlockBinding);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteCoreShaderStorageBlockBinding(uint @program, uint @storageBlockIndex, uint @storageBlockBinding)
    {
        var observation = StateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, false, -1, 1, false, false, ProgramInterface.ShaderStorageBlock, unchecked((int)@storageBlockIndex));
        bool returned = false;
        try
        {
            GL.ShaderStorageBlockBinding(@program, @storageBlockIndex, @storageBlockBinding);
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
