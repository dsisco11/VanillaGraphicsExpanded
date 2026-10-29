using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Propagates established generic and VAO-owned element assignments from exact native commands.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Reconciles generic and element-buffer effects together after a completed native binding command.</summary>
    internal static void ObserveBufferBinding(BufferTarget target, int buffer)
    {
        if (!OpenGLObservationContext.TryGet(out var cache, out _, false)) return;
        bool targetValid = cache.IsBufferTargetSupported(target);
        if (!targetValid && target == BufferTarget.ParameterBuffer && !GpuSupport.HasCachedCapabilitiesForCurrentContext)
            cache.MarkUncertainBufferRealization(buffer);
        bool elements = target == BufferTarget.ElementArrayBuffer;
        int vao = elements ? cache.GetCurrentVao() : 0;
        if (!targetValid) return;
        if (elements && vao == 0 && (!GpuSupport.HasCachedCapabilitiesForCurrentContext || GpuSupport.Graphics.CoreProfile))
        {
            if (!GpuSupport.HasCachedCapabilitiesForCurrentContext) cache.MarkUncertainBufferRealization(buffer);
            return;
        }
        bool established = buffer == 0 || cache.RealizeNativeBuffer(buffer, GpuSupport.HasCachedCapabilitiesForCurrentContext && !GpuSupport.Graphics.CoreProfile) is { IsRealized: true };
        bool sameElement = elements && cache.elementArrayBufferByVao.TryGetValue(vao, out int previous) && previous == buffer;
        var origin = Integration.RenderContextRegistry.CaptureResourceContext();
        if (targetValid) cache.bufferBindingByTarget.Remove(target);
        if (elements && !sameElement) ObserveVertexMetadata(vao, named: true, elementBinding: true);
        if (!established || !cache.OwnsKnowledgeFor(origin)
            || !Integration.RenderContextRegistry.IsCurrentResourceContext(origin)) return;
        if (elements) cache.elementArrayBufferByVao[vao] = buffer;
        else cache.bufferBindingByTarget[target] = buffer;
    }
    #endregion
}
