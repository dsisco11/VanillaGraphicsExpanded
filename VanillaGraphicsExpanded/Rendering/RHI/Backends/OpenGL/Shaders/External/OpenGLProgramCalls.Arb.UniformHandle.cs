using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Arb UniformHandle execution and affected metadata completion.</summary>
internal static unsafe partial class OpenGLProgramCalls
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteArbUniformHandle(int @location, int @count, ref long @value)
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

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteArbUniformHandle(int @location, int @count, long[] @value)
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

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteArbUniformHandle(int @location, int @count, long* @value)
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

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteArbUniformHandle(int @location, int @count, ref ulong @value)
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

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteArbUniformHandle(int @location, int @count, ulong[] @value)
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

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteArbUniformHandle(int @location, int @count, ulong* @value)
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

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteArbUniformHandle(int @location, long @value)
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

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteArbUniformHandle(int @location, ulong @value)
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
