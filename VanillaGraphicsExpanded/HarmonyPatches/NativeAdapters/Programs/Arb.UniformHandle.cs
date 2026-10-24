using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Programs;

/// <summary>Forwards GL.Arb.UniformHandle overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Arb
{
    #region Internal API
    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void UniformHandle(int @location, int @count, ref long @value)
    {
        var observation = StateCache.BeginNativeProgramMetadata(0, false, true, unchecked((int)@location), unchecked((int)@count), false, true);
        bool returned = false;
        try
        {
            GL.Arb.UniformHandle(@location, @count, ref @value);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void UniformHandle(int @location, int @count, long[] @value)
    {
        var observation = StateCache.BeginNativeProgramMetadata(0, false, true, unchecked((int)@location), unchecked((int)@count), false, true);
        bool returned = false;
        try
        {
            GL.Arb.UniformHandle(@location, @count, @value);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void UniformHandle(int @location, int @count, long* @value)
    {
        var observation = StateCache.BeginNativeProgramMetadata(0, false, true, unchecked((int)@location), unchecked((int)@count), false, true);
        bool returned = false;
        try
        {
            GL.Arb.UniformHandle(@location, @count, @value);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void UniformHandle(int @location, int @count, ref ulong @value)
    {
        var observation = StateCache.BeginNativeProgramMetadata(0, false, true, unchecked((int)@location), unchecked((int)@count), false, true);
        bool returned = false;
        try
        {
            GL.Arb.UniformHandle(@location, @count, ref @value);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void UniformHandle(int @location, int @count, ulong[] @value)
    {
        var observation = StateCache.BeginNativeProgramMetadata(0, false, true, unchecked((int)@location), unchecked((int)@count), false, true);
        bool returned = false;
        try
        {
            GL.Arb.UniformHandle(@location, @count, @value);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void UniformHandle(int @location, int @count, ulong* @value)
    {
        var observation = StateCache.BeginNativeProgramMetadata(0, false, true, unchecked((int)@location), unchecked((int)@count), false, true);
        bool returned = false;
        try
        {
            GL.Arb.UniformHandle(@location, @count, @value);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void UniformHandle(int @location, long @value)
    {
        var observation = StateCache.BeginNativeProgramMetadata(0, false, true, unchecked((int)@location), 1, false, true);
        bool returned = false;
        try
        {
            GL.Arb.UniformHandle(@location, @value);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void UniformHandle(int @location, ulong @value)
    {
        var observation = StateCache.BeginNativeProgramMetadata(0, false, true, unchecked((int)@location), 1, false, true);
        bool returned = false;
        try
        {
            GL.Arb.UniformHandle(@location, @value);
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
