using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns exact Core ProgramBinary execution and affected metadata completion.</summary>
internal static unsafe partial class OpenGLProgramCalls
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteCoreProgramBinary(int @program, BinaryFormat @binaryFormat, IntPtr @binary, int @length)
    {
        var observation = OpenGlStateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, false, -1, 1, true, false);
        bool returned = false;
        try
        {
            GL.ProgramBinary(@program, @binaryFormat, @binary, @length);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteCoreProgramBinary(uint @program, BinaryFormat @binaryFormat, IntPtr @binary, int @length)
    {
        var observation = OpenGlStateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, false, -1, 1, true, false);
        bool returned = false;
        try
        {
            GL.ProgramBinary(@program, @binaryFormat, @binary, @length);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteCoreProgramBinary<T2>(int @program, BinaryFormat @binaryFormat, ref T2 @binary, int @length)
        where T2 : struct
    {
        var observation = OpenGlStateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, false, -1, 1, true, false);
        bool returned = false;
        try
        {
            GL.ProgramBinary<T2>(@program, @binaryFormat, ref @binary, @length);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteCoreProgramBinary<T2>(int @program, BinaryFormat @binaryFormat, T2[,,] @binary, int @length)
        where T2 : struct
    {
        var observation = OpenGlStateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, false, -1, 1, true, false);
        bool returned = false;
        try
        {
            GL.ProgramBinary<T2>(@program, @binaryFormat, @binary, @length);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteCoreProgramBinary<T2>(int @program, BinaryFormat @binaryFormat, T2[,] @binary, int @length)
        where T2 : struct
    {
        var observation = OpenGlStateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, false, -1, 1, true, false);
        bool returned = false;
        try
        {
            GL.ProgramBinary<T2>(@program, @binaryFormat, @binary, @length);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteCoreProgramBinary<T2>(int @program, BinaryFormat @binaryFormat, T2[] @binary, int @length)
        where T2 : struct
    {
        var observation = OpenGlStateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, false, -1, 1, true, false);
        bool returned = false;
        try
        {
            GL.ProgramBinary<T2>(@program, @binaryFormat, @binary, @length);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteCoreProgramBinary<T2>(uint @program, BinaryFormat @binaryFormat, ref T2 @binary, int @length)
        where T2 : struct
    {
        var observation = OpenGlStateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, false, -1, 1, true, false);
        bool returned = false;
        try
        {
            GL.ProgramBinary<T2>(@program, @binaryFormat, ref @binary, @length);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteCoreProgramBinary<T2>(uint @program, BinaryFormat @binaryFormat, T2[,,] @binary, int @length)
        where T2 : struct
    {
        var observation = OpenGlStateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, false, -1, 1, true, false);
        bool returned = false;
        try
        {
            GL.ProgramBinary<T2>(@program, @binaryFormat, @binary, @length);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteCoreProgramBinary<T2>(uint @program, BinaryFormat @binaryFormat, T2[,] @binary, int @length)
        where T2 : struct
    {
        var observation = OpenGlStateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, false, -1, 1, true, false);
        bool returned = false;
        try
        {
            GL.ProgramBinary<T2>(@program, @binaryFormat, @binary, @length);
            returned = true;
        }
        finally
        {
            // A throwing native call keeps affected metadata unavailable.
            observation.Complete(returned);
        }
    }

    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal static void ExecuteCoreProgramBinary<T2>(uint @program, BinaryFormat @binaryFormat, T2[] @binary, int @length)
        where T2 : struct
    {
        var observation = OpenGlStateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, false, -1, 1, true, false);
        bool returned = false;
        try
        {
            GL.ProgramBinary<T2>(@program, @binaryFormat, @binary, @length);
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
