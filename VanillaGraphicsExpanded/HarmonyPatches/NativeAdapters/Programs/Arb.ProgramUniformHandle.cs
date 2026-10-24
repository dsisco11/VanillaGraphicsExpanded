using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Programs;

/// <summary>Forwards GL.Arb.ProgramUniformHandle overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Arb
{
    #region Internal API
    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void ProgramUniformHandle(int @program, int @location, int @count, ref long @values)
    {
        var observation = StateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, true, unchecked((int)@location), unchecked((int)@count), false, true);
        bool returned = false;
        try
        {
            GL.Arb.ProgramUniformHandle(@program, @location, @count, ref @values);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void ProgramUniformHandle(int @program, int @location, int @count, long[] @values)
    {
        var observation = StateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, true, unchecked((int)@location), unchecked((int)@count), false, true);
        bool returned = false;
        try
        {
            GL.Arb.ProgramUniformHandle(@program, @location, @count, @values);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void ProgramUniformHandle(int @program, int @location, int @count, long* @values)
    {
        var observation = StateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, true, unchecked((int)@location), unchecked((int)@count), false, true);
        bool returned = false;
        try
        {
            GL.Arb.ProgramUniformHandle(@program, @location, @count, @values);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void ProgramUniformHandle(int @program, int @location, long @value)
    {
        var observation = StateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, true, unchecked((int)@location), 1, false, true);
        bool returned = false;
        try
        {
            GL.Arb.ProgramUniformHandle(@program, @location, @value);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void ProgramUniformHandle(uint @program, int @location, int @count, ref ulong @values)
    {
        var observation = StateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, true, unchecked((int)@location), unchecked((int)@count), false, true);
        bool returned = false;
        try
        {
            GL.Arb.ProgramUniformHandle(@program, @location, @count, ref @values);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void ProgramUniformHandle(uint @program, int @location, int @count, ulong[] @values)
    {
        var observation = StateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, true, unchecked((int)@location), unchecked((int)@count), false, true);
        bool returned = false;
        try
        {
            GL.Arb.ProgramUniformHandle(@program, @location, @count, @values);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void ProgramUniformHandle(uint @program, int @location, int @count, ulong* @values)
    {
        var observation = StateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, true, unchecked((int)@location), unchecked((int)@count), false, true);
        bool returned = false;
        try
        {
            GL.Arb.ProgramUniformHandle(@program, @location, @count, @values);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void ProgramUniformHandle(uint @program, int @location, ulong @value)
    {
        var observation = StateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, true, unchecked((int)@location), 1, false, true);
        bool returned = false;
        try
        {
            GL.Arb.ProgramUniformHandle(@program, @location, @value);
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
