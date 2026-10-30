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
        bool? targetValid = cache.ClassifyBufferTarget(target);
        if (targetValid is null)
        {
            // Without optional-target admission the native selection may be old or supplied.
            // Once its selector is lost, later bound storage cannot identify either candidate;
            // withdraw only those two original buffers now, rather than scan or guess later.
            cache.TryGetCachedBoundBuffer(target, out int previousBinding);
            cache.WithdrawUncertainBufferTarget(buffer);
            if (previousBinding != buffer) cache.WithdrawUncertainBufferTarget(previousBinding);
            cache.bufferBindingByTarget.Remove(target);
            return;
        }
        bool elements = target == BufferTarget.ElementArrayBuffer;
        int vao = elements ? cache.GetCurrentVao() : 0;
        if (targetValid == false) return;
        if (elements && vao == 0 && (!GpuSupport.HasCachedCapabilitiesForCurrentContext || GpuSupport.Graphics.CoreProfile))
        {
            if (!GpuSupport.HasCachedCapabilitiesForCurrentContext) cache.MarkUncertainBufferRealization(buffer);
            return;
        }
        bool established = buffer == 0 || cache.RealizeNativeBuffer(buffer, GpuSupport.HasCachedCapabilitiesForCurrentContext && !GpuSupport.Graphics.CoreProfile) is { IsRealized: true };
        bool sameElement = elements && cache.elementArrayBufferByVao.TryGetValue(vao, out int previous) && previous == buffer;
        var origin = Integration.RenderContextRegistry.CaptureResourceContext();
        cache.bufferBindingByTarget.Remove(target);
        if (elements && !sameElement) ObserveVertexMetadata(vao, named: true, elementBinding: true);
        if (!established || !cache.OwnsKnowledgeFor(origin)
            || !Integration.RenderContextRegistry.IsCurrentResourceContext(origin)) return;
        if (elements) cache.elementArrayBufferByVao[vao] = buffer;
        else cache.bufferBindingByTarget[target] = buffer;
    }
    #endregion

    #region Private
    /// <summary>Withdraws one possible optional-selector target without discovering or registering another identity.</summary>
    private void WithdrawUncertainBufferTarget(int id)
    {
        if (id == 0) return;
        InterruptNativeResourceOperation(GpuResourceKind.Buffer, id);
        bufferMappings.Remove(id);
        if (RHIResourceManagerSystem.CurrentManager?.FindObservedResource(GpuResourceKind.Buffer, id)
            is not GpuResourceMetadataEntry<GpuBufferMetadata> entry) return;
        if (!InterruptNativeMetadataCompletion(entry, GpuMetadataChange.Storage)) entry.Withdraw(GpuMetadataChange.Storage);
    }
    #endregion
}
