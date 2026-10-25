using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Arb LinkProgram execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal void ExecuteArbLinkProgram(int @programObj)
    {
        var observation = StateCache.BeginNativeProgramMetadata(unchecked((int)@programObj), true, false, -1, 1, true, false);
        bool returned = false;
        try
        {
            GL.Arb.LinkProgram(@programObj);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal void ExecuteArbLinkProgram(uint @programObj)
    {
        var observation = StateCache.BeginNativeProgramMetadata(unchecked((int)@programObj), true, false, -1, 1, true, false);
        bool returned = false;
        try
        {
            GL.Arb.LinkProgram(@programObj);
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
