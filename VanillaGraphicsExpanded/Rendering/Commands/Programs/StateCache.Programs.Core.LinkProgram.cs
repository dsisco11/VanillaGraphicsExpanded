using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Core LinkProgram execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal void ExecuteCoreLinkProgram(int @program)
    {
        var observation = StateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, false, -1, 1, true, false);
        bool returned = false;
        try
        {
            GL.LinkProgram(@program);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal void ExecuteCoreLinkProgram(uint @program)
    {
        var observation = StateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, false, -1, 1, true, false);
        bool returned = false;
        try
        {
            GL.LinkProgram(@program);
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
