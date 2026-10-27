using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Arb ProgramUniformHandle execution and affected metadata completion.</summary>
internal static unsafe partial class OpenGLProgramCalls
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteArbProgramUniformHandle(int @program, int @location, int @count, ref long @values)
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

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteArbProgramUniformHandle(int @program, int @location, int @count, long[] @values)
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

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteArbProgramUniformHandle(int @program, int @location, int @count, long* @values)
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

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteArbProgramUniformHandle(int @program, int @location, long @value)
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

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteArbProgramUniformHandle(uint @program, int @location, int @count, ref ulong @values)
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

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteArbProgramUniformHandle(uint @program, int @location, int @count, ulong[] @values)
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

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteArbProgramUniformHandle(uint @program, int @location, int @count, ulong* @values)
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

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteArbProgramUniformHandle(uint @program, int @location, ulong @value)
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
