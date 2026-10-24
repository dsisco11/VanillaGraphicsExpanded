using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Programs;

/// <summary>Forwards GL.Ext.ActiveProgram overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void ActiveProgram(int @program)
    {
        var observation = StateCache.BeginNativeProgramSelection(false);
        bool returned = false;
        try
        {
            GL.Ext.ActiveProgram(@program);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void ActiveProgram(uint @program)
    {
        var observation = StateCache.BeginNativeProgramSelection(false);
        bool returned = false;
        try
        {
            GL.Ext.ActiveProgram(@program);
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
