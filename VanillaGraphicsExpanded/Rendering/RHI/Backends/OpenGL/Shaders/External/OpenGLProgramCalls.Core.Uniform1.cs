using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Core Uniform1 execution and affected metadata completion.</summary>
internal static unsafe partial class OpenGLProgramCalls
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteCoreUniform1(int @location, int @count, ref int @value)
    {
        var observation = OpenGlStateCache.BeginNativeProgramMetadata(0, false, true, unchecked((int)@location), unchecked((int)@count), false, false);
        bool returned = false;
        try
        {
            GL.Uniform1(@location, @count, ref @value);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteCoreUniform1(int @location, int @count, int[] @value)
    {
        var observation = OpenGlStateCache.BeginNativeProgramMetadata(0, false, true, unchecked((int)@location), unchecked((int)@count), false, false);
        bool returned = false;
        try
        {
            GL.Uniform1(@location, @count, @value);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteCoreUniform1(int @location, int @count, int* @value)
    {
        var observation = OpenGlStateCache.BeginNativeProgramMetadata(0, false, true, unchecked((int)@location), unchecked((int)@count), false, false);
        bool returned = false;
        try
        {
            GL.Uniform1(@location, @count, @value);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteCoreUniform1(int @location, int @v0)
    {
        var observation = OpenGlStateCache.BeginNativeProgramMetadata(0, false, true, unchecked((int)@location), 1, false, false);
        bool returned = false;
        try
        {
            GL.Uniform1(@location, @v0);
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
