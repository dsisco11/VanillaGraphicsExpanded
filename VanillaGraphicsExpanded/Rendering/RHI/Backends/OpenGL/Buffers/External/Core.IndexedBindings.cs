using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Forwards exact external indexed selectors without managed suppression or error translation.</summary>
internal static unsafe partial class OpenGLBufferBindingCalls
{
    #region Internal API
    /// <summary>Executes once and withdraws the affected generic/indexed knowledge, including on failure.</summary>
    internal static void BindBufferBase(BufferRangeTarget target, int slot, int buffer)
    {
        var cache = EngineStateCalls.CurrentCache;
        cache.ForgetBufferBinding((BufferTarget)target, slot);
        try { GL.BindBufferBase(target, slot, buffer); }
        finally { cache.ForgetBufferBinding((BufferTarget)target, slot); }
    }
    /// <summary>Preserves exact pointer-width range arguments and the native error queue.</summary>
    internal static void BindBufferRange(BufferRangeTarget target, int slot, int buffer, nint offset, nint size)
    {
        var cache = EngineStateCalls.CurrentCache;
        cache.ForgetBufferBinding((BufferTarget)target, slot);
        try { GL.BindBufferRange(target, slot, buffer, offset, size); }
        finally { cache.ForgetBufferBinding((BufferTarget)target, slot); }
    }
    #endregion
}
