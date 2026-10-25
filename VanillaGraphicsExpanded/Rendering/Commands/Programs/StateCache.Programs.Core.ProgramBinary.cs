using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns exact Core ProgramBinary execution and affected metadata completion.</summary>
internal sealed unsafe partial class StateCache
{
    #region Internal API
    /// <summary>Executes the exact external overload and completes affected metadata publication.</summary>
    internal void ExecuteCoreProgramBinary(int @program, BinaryFormat @binaryFormat, IntPtr @binary, int @length)
    {
        var observation = StateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, false, -1, 1, true, false);
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
    internal void ExecuteCoreProgramBinary(uint @program, BinaryFormat @binaryFormat, IntPtr @binary, int @length)
    {
        var observation = StateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, false, -1, 1, true, false);
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
    internal void ExecuteCoreProgramBinary<T2>(int @program, BinaryFormat @binaryFormat, ref T2 @binary, int @length)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, false, -1, 1, true, false);
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
    internal void ExecuteCoreProgramBinary<T2>(int @program, BinaryFormat @binaryFormat, T2[,,] @binary, int @length)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, false, -1, 1, true, false);
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
    internal void ExecuteCoreProgramBinary<T2>(int @program, BinaryFormat @binaryFormat, T2[,] @binary, int @length)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, false, -1, 1, true, false);
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
    internal void ExecuteCoreProgramBinary<T2>(int @program, BinaryFormat @binaryFormat, T2[] @binary, int @length)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, false, -1, 1, true, false);
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
    internal void ExecuteCoreProgramBinary<T2>(uint @program, BinaryFormat @binaryFormat, ref T2 @binary, int @length)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, false, -1, 1, true, false);
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
    internal void ExecuteCoreProgramBinary<T2>(uint @program, BinaryFormat @binaryFormat, T2[,,] @binary, int @length)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, false, -1, 1, true, false);
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
    internal void ExecuteCoreProgramBinary<T2>(uint @program, BinaryFormat @binaryFormat, T2[,] @binary, int @length)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, false, -1, 1, true, false);
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
    internal void ExecuteCoreProgramBinary<T2>(uint @program, BinaryFormat @binaryFormat, T2[] @binary, int @length)
        where T2 : struct
    {
        var observation = StateCache.BeginNativeProgramMetadata(unchecked((int)@program), true, false, -1, 1, true, false);
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
