using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Programs;

/// <summary>Forwards GL.ProgramBinary overloads through their existing StateCache observation contract.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void ProgramBinary(int @program, BinaryFormat @binaryFormat, IntPtr @binary, int @length)
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

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void ProgramBinary(uint @program, BinaryFormat @binaryFormat, IntPtr @binary, int @length)
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

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void ProgramBinary<T2>(int @program, BinaryFormat @binaryFormat, ref T2 @binary, int @length)
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

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void ProgramBinary<T2>(int @program, BinaryFormat @binaryFormat, T2[,,] @binary, int @length)
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

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void ProgramBinary<T2>(int @program, BinaryFormat @binaryFormat, T2[,] @binary, int @length)
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

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void ProgramBinary<T2>(int @program, BinaryFormat @binaryFormat, T2[] @binary, int @length)
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

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void ProgramBinary<T2>(uint @program, BinaryFormat @binaryFormat, ref T2 @binary, int @length)
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

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void ProgramBinary<T2>(uint @program, BinaryFormat @binaryFormat, T2[,,] @binary, int @length)
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

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void ProgramBinary<T2>(uint @program, BinaryFormat @binaryFormat, T2[,] @binary, int @length)
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

    /// <summary>Preserves the native arguments and publishes observed metadata after dispatch.</summary>
    internal static void ProgramBinary<T2>(uint @program, BinaryFormat @binaryFormat, T2[] @binary, int @length)
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
