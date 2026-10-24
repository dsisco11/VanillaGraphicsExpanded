using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Programs;

/// <summary>Forwards GL.BindProgramPipeline overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void BindProgramPipeline(int @pipeline)
    {
        var observation = StateCache.BeginNativeProgramSelection(true);
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

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void BindProgramPipeline(uint @pipeline)
    {
        var observation = StateCache.BeginNativeProgramSelection(true);
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
