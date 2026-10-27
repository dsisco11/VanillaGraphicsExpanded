using System;
using GL = OpenTK.Graphics.OpenGL4.GL;
using Native = OpenTK.Graphics.OpenGL4;
using Legacy = OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Executes selector changes using the existing cache's complete suppression keys.</summary>
internal static class OpenGLBufferBindings
{
    #region Internal API
    /// <summary>Preserves generic and VAO-owned element binding semantics without a second cache.</summary>
    internal static void BindBuffer(StateCache cache, Legacy.BufferTarget target, int buffer)
    {
        var context = RHICommandContext.Current.OpenGL;
        context.RequireContext();
        if (!cache.NeedsBufferBinding(target, buffer)) return;
        var native = Target(target);
        if (target == Legacy.BufferTarget.ElementArrayBuffer)
        {
            var observation = StateCache.BeginNativeVertexMetadata(0, named: false, elementBinding: true);
            bool returned = false;
            try { GL.BindBuffer(native, buffer); context.RequireContext(); returned = true; }
            finally { observation.Complete(returned); }
            observation.EstablishElementBinding(buffer);
            return;
        }
        cache.ForgetBufferBinding(target);
        GL.BindBuffer(native, buffer);
        context.RequireContext();
        cache.EstablishBufferBinding(target, buffer);
    }

    /// <summary>Uses a full indexed key and establishes the generic binding side effect.</summary>
    internal static void BindBufferBase(StateCache cache, Legacy.BufferRangeTarget target, int slot, int buffer)
    {
        var context = RHICommandContext.Current.OpenGL;
        context.RequireContext();
        if (!cache.NeedsIndexedBufferBinding(target, slot, buffer, 0, 0, false)) return;
        var native = IndexedTarget(target);
        cache.ForgetBufferBinding((Legacy.BufferTarget)target, slot);
        GL.BindBufferBase(native, slot, buffer);
        context.RequireContext();
        cache.EstablishIndexedBufferBinding(target, slot, buffer, 0, 0, false);
    }

    /// <summary>Preserves native-width ranges and updates knowledge only after dispatch.</summary>
    internal static void BindBufferRange(StateCache cache, Legacy.BufferRangeTarget target, int slot, int buffer, nint offset, nint size)
    {
        var context = RHICommandContext.Current.OpenGL;
        context.RequireContext();
        if (!cache.NeedsIndexedBufferBinding(target, slot, buffer, offset, size, true)) return;
        var native = IndexedTarget(target);
        cache.ForgetBufferBinding((Legacy.BufferTarget)target, slot);
        GL.BindBufferRange(native, slot, buffer, offset, size);
        context.RequireContext();
        cache.EstablishIndexedBufferBinding(target, slot, buffer, offset, size, true);
    }
    #endregion

    #region Private
    /// <summary>Translates existing cache/integration selectors using native enum members.</summary>
    private static Native.BufferTarget Target(Legacy.BufferTarget target) => target switch
    {
        Legacy.BufferTarget.ArrayBuffer => Native.BufferTarget.ArrayBuffer,
        Legacy.BufferTarget.ElementArrayBuffer => Native.BufferTarget.ElementArrayBuffer,
        Legacy.BufferTarget.UniformBuffer => Native.BufferTarget.UniformBuffer,
        Legacy.BufferTarget.ShaderStorageBuffer => Native.BufferTarget.ShaderStorageBuffer,
        Legacy.BufferTarget.AtomicCounterBuffer => Native.BufferTarget.AtomicCounterBuffer,
        Legacy.BufferTarget.CopyReadBuffer => Native.BufferTarget.CopyReadBuffer,
        Legacy.BufferTarget.CopyWriteBuffer => Native.BufferTarget.CopyWriteBuffer,
        Legacy.BufferTarget.DrawIndirectBuffer => Native.BufferTarget.DrawIndirectBuffer,
        Legacy.BufferTarget.DispatchIndirectBuffer => Native.BufferTarget.DispatchIndirectBuffer,
        Legacy.BufferTarget.ParameterBuffer => Native.BufferTarget.ParameterBuffer,
        Legacy.BufferTarget.PixelPackBuffer => Native.BufferTarget.PixelPackBuffer,
        Legacy.BufferTarget.PixelUnpackBuffer => Native.BufferTarget.PixelUnpackBuffer,
        Legacy.BufferTarget.TransformFeedbackBuffer => Native.BufferTarget.TransformFeedbackBuffer,
        Legacy.BufferTarget.TextureBuffer => Native.BufferTarget.TextureBuffer,
        Legacy.BufferTarget.QueryBuffer => Native.BufferTarget.QueryBuffer,
        _ => throw new ArgumentOutOfRangeException(nameof(target))
    };

    /// <summary>Translates the four supported indexed buffer namespaces.</summary>
    private static Native.BufferRangeTarget IndexedTarget(Legacy.BufferRangeTarget target) => target switch
    {
        Legacy.BufferRangeTarget.UniformBuffer => Native.BufferRangeTarget.UniformBuffer,
        Legacy.BufferRangeTarget.ShaderStorageBuffer => Native.BufferRangeTarget.ShaderStorageBuffer,
        Legacy.BufferRangeTarget.AtomicCounterBuffer => Native.BufferRangeTarget.AtomicCounterBuffer,
        Legacy.BufferRangeTarget.TransformFeedbackBuffer => Native.BufferRangeTarget.TransformFeedbackBuffer,
        _ => throw new ArgumentOutOfRangeException(nameof(target))
    };
    #endregion
}
