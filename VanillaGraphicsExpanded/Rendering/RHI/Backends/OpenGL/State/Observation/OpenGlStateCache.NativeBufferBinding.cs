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
        var cache = OpenGLObservationContext.CurrentCache;
        bool targetValid = Enum.IsDefined(target) && (target != BufferTarget.ParameterBuffer
            || GpuSupport.Graphics.ApiVersion is { Major: > 4 } or { Major: 4, Minor: >= 6 }
            || GpuSupport.Supports("GL_ARB_indirect_parameters"));
        bool established = targetValid && cache.HasEstablishedNativeIdentity(GpuResourceKind.Buffer, buffer);
        bool elements = target == BufferTarget.ElementArrayBuffer;
        int vao = elements ? cache.GetCurrentVao() : 0;
        if (elements && vao == 0 && GpuSupport.Graphics.CoreProfile) established = false;
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
