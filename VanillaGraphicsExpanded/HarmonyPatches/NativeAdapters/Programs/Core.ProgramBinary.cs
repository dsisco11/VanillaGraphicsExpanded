using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.Programs;

/// <summary>Routes exact Core overloads through their command execution boundary.</summary>
internal static unsafe partial class Core
{
    #region Internal API
    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void ProgramBinary(int @program, BinaryFormat @binaryFormat, IntPtr @binary, int @length)
        => OpenGLProgramCalls.ExecuteCoreProgramBinary(@program, @binaryFormat, @binary, @length);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void ProgramBinary(uint @program, BinaryFormat @binaryFormat, IntPtr @binary, int @length)
        => OpenGLProgramCalls.ExecuteCoreProgramBinary(@program, @binaryFormat, @binary, @length);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void ProgramBinary<T2>(int @program, BinaryFormat @binaryFormat, ref T2 @binary, int @length)
        where T2 : struct
        => OpenGLProgramCalls.ExecuteCoreProgramBinary<T2>(@program, @binaryFormat, ref @binary, @length);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void ProgramBinary<T2>(int @program, BinaryFormat @binaryFormat, T2[,,] @binary, int @length)
        where T2 : struct
        => OpenGLProgramCalls.ExecuteCoreProgramBinary<T2>(@program, @binaryFormat, @binary, @length);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void ProgramBinary<T2>(int @program, BinaryFormat @binaryFormat, T2[,] @binary, int @length)
        where T2 : struct
        => OpenGLProgramCalls.ExecuteCoreProgramBinary<T2>(@program, @binaryFormat, @binary, @length);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void ProgramBinary<T2>(int @program, BinaryFormat @binaryFormat, T2[] @binary, int @length)
        where T2 : struct
        => OpenGLProgramCalls.ExecuteCoreProgramBinary<T2>(@program, @binaryFormat, @binary, @length);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void ProgramBinary<T2>(uint @program, BinaryFormat @binaryFormat, ref T2 @binary, int @length)
        where T2 : struct
        => OpenGLProgramCalls.ExecuteCoreProgramBinary<T2>(@program, @binaryFormat, ref @binary, @length);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void ProgramBinary<T2>(uint @program, BinaryFormat @binaryFormat, T2[,,] @binary, int @length)
        where T2 : struct
        => OpenGLProgramCalls.ExecuteCoreProgramBinary<T2>(@program, @binaryFormat, @binary, @length);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void ProgramBinary<T2>(uint @program, BinaryFormat @binaryFormat, T2[,] @binary, int @length)
        where T2 : struct
        => OpenGLProgramCalls.ExecuteCoreProgramBinary<T2>(@program, @binaryFormat, @binary, @length);

    /// <summary>Forwards the exact signature to the backend execution boundary.</summary>
    internal static void ProgramBinary<T2>(uint @program, BinaryFormat @binaryFormat, T2[] @binary, int @length)
        where T2 : struct
        => OpenGLProgramCalls.ExecuteCoreProgramBinary<T2>(@program, @binaryFormat, @binary, @length);
    #endregion
}
