using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Programs;

/// <summary>Forwards GL.Arb.LinkProgram overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Arb
{
    #region Internal API
    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void LinkProgram(int @programObj)
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

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void LinkProgram(uint @programObj)
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
