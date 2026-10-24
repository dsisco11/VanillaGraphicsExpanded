using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Programs;

/// <summary>Forwards GL.Arb.Uniform1 overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Arb
{
    #region Internal API
    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void Uniform1(int @location, int @count, ref int @value)
    {
        var observation = StateCache.BeginNativeProgramMetadata(0, false, true, unchecked((int)@location), unchecked((int)@count), false, false);
        bool returned = false;
        try
        {
            GL.Arb.Uniform1(@location, @count, ref @value);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void Uniform1(int @location, int @count, int[] @value)
    {
        var observation = StateCache.BeginNativeProgramMetadata(0, false, true, unchecked((int)@location), unchecked((int)@count), false, false);
        bool returned = false;
        try
        {
            GL.Arb.Uniform1(@location, @count, @value);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void Uniform1(int @location, int @count, int* @value)
    {
        var observation = StateCache.BeginNativeProgramMetadata(0, false, true, unchecked((int)@location), unchecked((int)@count), false, false);
        bool returned = false;
        try
        {
            GL.Arb.Uniform1(@location, @count, @value);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void Uniform1(int @location, int @v0)
    {
        var observation = StateCache.BeginNativeProgramMetadata(0, false, true, unchecked((int)@location), 1, false, false);
        bool returned = false;
        try
        {
            GL.Arb.Uniform1(@location, @v0);
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
