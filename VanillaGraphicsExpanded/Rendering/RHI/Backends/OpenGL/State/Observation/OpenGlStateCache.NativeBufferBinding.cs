using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Propagates established generic and VAO-owned element assignments from exact native commands.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Captures original selector/realization proof and withdraws only fields the native target can affect.</summary>
    internal static NativeBufferBinding BeginNativeBufferBinding(BufferTarget target, int buffer)
    {
        var cache = EngineStateCalls.CurrentCache;
        bool targetValid = Enum.IsDefined(target) && (target != BufferTarget.ParameterBuffer
            || GpuSupport.Graphics.ApiVersion is { Major: > 4 } or { Major: 4, Minor: >= 6 }
            || GpuSupport.Supports("GL_ARB_indirect_parameters"));
        bool established = targetValid && cache.HasEstablishedNativeIdentity(GpuResourceKind.Buffer, buffer);
        bool elements = target == BufferTarget.ElementArrayBuffer;
        int vao = elements ? cache.GetCurrentVao() : 0;
        if (elements && vao == 0 && GpuSupport.Graphics.CoreProfile) established = false;
        bool sameElement = elements && cache.elementArrayBufferByVao.TryGetValue(vao, out int previous) && previous == buffer;
        var vertex = elements && !sameElement ? BeginNativeVertexMetadata(vao, named: true, elementBinding: true) : default;
        if (targetValid) cache.bufferBindingByTarget.Remove(target);
        return new(cache, target, buffer, established, vertex, elements, !sameElement, vao,
            Integration.RenderContextRegistry.CaptureResourceContext());
    }
    #endregion

    /// <summary>Retains original selector authority and the single empty-schema VAO revision operation.</summary>
    internal readonly struct NativeBufferBinding(OpenGlStateCache cache, BufferTarget target, int buffer,
        bool established, NativeVertexMetadata vertex, bool elements, bool changesElement, int vao,
        (nint Handle, long Generation) origin)
    {
        #region Internal API
        /// <summary>Publishes proven assignments, preserving both generic and VAO-local aliases without recapture.</summary>
        internal void Complete(bool returned)
        {
            if (elements && changesElement) vertex.Complete(returned);
            if (!returned || !established || !cache.OwnsKnowledgeFor(origin) || !Integration.RenderContextRegistry.IsCurrentResourceContext(origin)) return;
            if (elements) cache.elementArrayBufferByVao[vao] = buffer;
            else cache.bufferBindingByTarget[target] = buffer;
        }
        #endregion
    }
}
