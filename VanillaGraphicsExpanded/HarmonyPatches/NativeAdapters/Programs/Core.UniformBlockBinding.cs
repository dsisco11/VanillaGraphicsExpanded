using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Programs;

/// <summary>Forwards GL.UniformBlockBinding overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void UniformBlockBinding(int @program, int @uniformBlockIndex, int @uniformBlockBinding)
    {
        var observation = StateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, false, -1, 1, false, false);
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

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void UniformBlockBinding(uint @program, uint @uniformBlockIndex, uint @uniformBlockBinding)
    {
        var observation = StateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, false, -1, 1, false, false);
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
