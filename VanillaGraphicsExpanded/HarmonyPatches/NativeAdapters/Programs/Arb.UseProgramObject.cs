using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Programs;

/// <summary>Forwards GL.Arb.UseProgramObject overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Arb
{
    #region Internal API
    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void UseProgramObject(int @programObj)
    {
        var observation = StateCache.BeginNativeProgramSelection(false);
        bool returned = false;
        try
        {
            GL.Arb.UseProgramObject(@programObj);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void UseProgramObject(uint @programObj)
    {
        var observation = StateCache.BeginNativeProgramSelection(false);
        bool returned = false;
        try
        {
            GL.Arb.UseProgramObject(@programObj);
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
