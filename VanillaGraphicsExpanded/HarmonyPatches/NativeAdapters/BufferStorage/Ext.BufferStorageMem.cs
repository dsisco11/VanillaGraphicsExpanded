using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.BufferStorage;

/// <summary>Forwards GL.Ext.BufferStorageMem overloads through their shared metadata observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void BufferStorageMem(BufferTargetArb @target, int @size, int @memory, long @offset)
        => OpenGLBufferStorageCalls.ExecuteExtBufferStorageMem(@target, @size, @memory, @offset);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void BufferStorageMem(BufferTargetArb @target, int @size, uint @memory, ulong @offset)
        => OpenGLBufferStorageCalls.ExecuteExtBufferStorageMem(@target, @size, @memory, @offset);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void BufferStorageMem(BufferTargetArb @target, IntPtr @size, int @memory, long @offset)
        => OpenGLBufferStorageCalls.ExecuteExtBufferStorageMem(@target, @size, @memory, @offset);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void BufferStorageMem(BufferTargetArb @target, IntPtr @size, uint @memory, ulong @offset)
        => OpenGLBufferStorageCalls.ExecuteExtBufferStorageMem(@target, @size, @memory, @offset);

    #endregion
}
