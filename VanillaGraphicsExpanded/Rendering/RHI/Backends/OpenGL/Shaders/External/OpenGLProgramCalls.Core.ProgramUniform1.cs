using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Core ProgramUniform1 execution and affected metadata completion.</summary>
internal static unsafe partial class OpenGLProgramCalls
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteCoreProgramUniform1(int @program, int @location, int @count, ref int @value)
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

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteCoreProgramUniform1(int @program, int @location, int @count, int[] @value)
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

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteCoreProgramUniform1(int @program, int @location, int @count, int* @value)
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

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteCoreProgramUniform1(int @program, int @location, int @v0)
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

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteCoreProgramUniform1(uint @program, int @location, int @count, ref int @value)
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

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteCoreProgramUniform1(uint @program, int @location, int @count, int[] @value)
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

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteCoreProgramUniform1(uint @program, int @location, int @count, int* @value)
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

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteCoreProgramUniform1(uint @program, int @location, int @v0)
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
