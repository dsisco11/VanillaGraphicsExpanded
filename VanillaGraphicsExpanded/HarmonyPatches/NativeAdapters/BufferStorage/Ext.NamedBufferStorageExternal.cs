using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters.BufferStorage;

/// <summary>Forwards GL.Ext.NamedBufferStorageExternal overloads through their shared metadata observation contract.</summary>
internal static unsafe partial class Ext
{
    #region Internal API
    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorageExternal(int @buffer, IntPtr @offset, int @size, IntPtr @clientBuffer, BufferStorageMask @flags)
        => OpenGLBufferStorageCalls.ExecuteExtNamedBufferStorageExternal(@buffer, @offset, @size, @clientBuffer, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorageExternal(int @buffer, IntPtr @offset, int @size, IntPtr @clientBuffer, MapBufferUsageMask @flags)
        => OpenGLBufferStorageCalls.ExecuteExtNamedBufferStorageExternal(@buffer, @offset, @size, @clientBuffer, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorageExternal(int @buffer, IntPtr @offset, IntPtr @size, IntPtr @clientBuffer, BufferStorageMask @flags)
        => OpenGLBufferStorageCalls.ExecuteExtNamedBufferStorageExternal(@buffer, @offset, @size, @clientBuffer, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorageExternal(int @buffer, IntPtr @offset, IntPtr @size, IntPtr @clientBuffer, MapBufferUsageMask @flags)
        => OpenGLBufferStorageCalls.ExecuteExtNamedBufferStorageExternal(@buffer, @offset, @size, @clientBuffer, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorageExternal(uint @buffer, IntPtr @offset, int @size, IntPtr @clientBuffer, BufferStorageMask @flags)
        => OpenGLBufferStorageCalls.ExecuteExtNamedBufferStorageExternal(@buffer, @offset, @size, @clientBuffer, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorageExternal(uint @buffer, IntPtr @offset, int @size, IntPtr @clientBuffer, MapBufferUsageMask @flags)
        => OpenGLBufferStorageCalls.ExecuteExtNamedBufferStorageExternal(@buffer, @offset, @size, @clientBuffer, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorageExternal(uint @buffer, IntPtr @offset, IntPtr @size, IntPtr @clientBuffer, BufferStorageMask @flags)
        => OpenGLBufferStorageCalls.ExecuteExtNamedBufferStorageExternal(@buffer, @offset, @size, @clientBuffer, @flags);

    /// <summary>Forwards the exact signature to the OpenGL backend execution boundary.</summary>
    internal static void NamedBufferStorageExternal(uint @buffer, IntPtr @offset, IntPtr @size, IntPtr @clientBuffer, MapBufferUsageMask @flags)
        => OpenGLBufferStorageCalls.ExecuteExtNamedBufferStorageExternal(@buffer, @offset, @size, @clientBuffer, @flags);

    #endregion
}
