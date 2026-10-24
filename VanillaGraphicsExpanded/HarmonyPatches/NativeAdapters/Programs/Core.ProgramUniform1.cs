using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Programs;

/// <summary>Forwards GL.ProgramUniform1 overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void ProgramUniform1(int @program, int @location, int @count, ref int @value)
    {
        var observation = StateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, true, unchecked((int)@location), unchecked((int)@count), false, false);
        bool returned = false;
        try
        {
            GL.ProgramUniform1(@program, @location, @count, ref @value);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void ProgramUniform1(int @program, int @location, int @count, int[] @value)
    {
        var observation = StateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, true, unchecked((int)@location), unchecked((int)@count), false, false);
        bool returned = false;
        try
        {
            GL.ProgramUniform1(@program, @location, @count, @value);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void ProgramUniform1(int @program, int @location, int @count, int* @value)
    {
        var observation = StateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, true, unchecked((int)@location), unchecked((int)@count), false, false);
        bool returned = false;
        try
        {
            GL.ProgramUniform1(@program, @location, @count, @value);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void ProgramUniform1(int @program, int @location, int @v0)
    {
        var observation = StateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, true, unchecked((int)@location), 1, false, false);
        bool returned = false;
        try
        {
            GL.ProgramUniform1(@program, @location, @v0);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void ProgramUniform1(uint @program, int @location, int @count, ref int @value)
    {
        var observation = StateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, true, unchecked((int)@location), unchecked((int)@count), false, false);
        bool returned = false;
        try
        {
            GL.ProgramUniform1(@program, @location, @count, ref @value);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void ProgramUniform1(uint @program, int @location, int @count, int[] @value)
    {
        var observation = StateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, true, unchecked((int)@location), unchecked((int)@count), false, false);
        bool returned = false;
        try
        {
            GL.ProgramUniform1(@program, @location, @count, @value);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void ProgramUniform1(uint @program, int @location, int @count, int* @value)
    {
        var observation = StateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, true, unchecked((int)@location), unchecked((int)@count), false, false);
        bool returned = false;
        try
        {
            GL.ProgramUniform1(@program, @location, @count, @value);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void ProgramUniform1(uint @program, int @location, int @v0)
    {
        var observation = StateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, true, unchecked((int)@location), 1, false, false);
        bool returned = false;
        try
        {
            GL.ProgramUniform1(@program, @location, @v0);
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
