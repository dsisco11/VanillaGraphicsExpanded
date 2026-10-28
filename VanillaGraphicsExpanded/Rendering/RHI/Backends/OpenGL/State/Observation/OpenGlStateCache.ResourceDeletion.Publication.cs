using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Reconciles successful native name deletion with canonical identity and resource-specific binding effects.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Preserves the existing framebuffer configuration guard before a native deletion command.</summary>
    internal void PrepareResourceDeletion(GpuResourceKind kind, int id)
    {
        if (kind == GpuResourceKind.Framebuffer) RequireMutableFramebufferConfiguration(id);
    }

    /// <summary>Retires only proven deleted names; exact adapters may supply an outcome from their original native loader.</summary>
    internal void CompleteResourceDeletion(GpuResourceKind kind, nint nativeId, bool? observedDeletion = null)
    {
        if (nativeId == 0 || !(observedDeletion ?? WasResourceDeleted(kind, nativeId))) return;
        CancelResourceOperation(kind, nativeId);
        GpuResourceManagerSystem.CurrentManager?.ObserveDeletion(kind, nativeId);
        int id = unchecked((int)nativeId);
        RHI.Backends.OpenGL.OpenGLRHIContext.NotifyResourceDeleted(this, kind, id);
        // Reconcile only the bindings whose native command has an implicit unbinding effect.
        switch (kind)
        {
            case GpuResourceKind.Texture:
                RecordBoundaryRetirement(EPipelineState.TextureBindings, id);
                InvalidateImageTexture(id);
                if (textureBindingsByUnit is null) break;
                foreach (var bindings in textureBindingsByUnit)
                {
                    if (bindings is null) continue;
                    foreach (var binding in bindings)
                        if (binding.Value == id) bindings[binding.Key] = 0;
                }
                break;
            case GpuResourceKind.Buffer:
                ForgetDeletedBuffer(id);
                break;
            case GpuResourceKind.Renderbuffer:
                RecordBoundaryRetirement(EPipelineState.RenderbufferBinding, id);
                if (currentRenderbuffer == id) currentRenderbuffer = 0;
                break;
            case GpuResourceKind.Sampler:
                RecordBoundaryRetirement(EPipelineState.SamplerBindings, id);
                if (samplerBindingByUnit is not null)
                    for (int unit = 0; unit < samplerBindingByUnit.Length; unit++)
                        if (samplerBindingByUnit[unit] == id) samplerBindingByUnit[unit] = 0;
                break;
            case GpuResourceKind.Framebuffer:
                InvalidateFramebufferDrawRouting(id);
                RecordBoundaryRetirement(EPipelineState.FramebufferBindings, id);
                if (currentReadFramebuffer == id) currentReadFramebuffer = 0;
                if (currentDrawFramebuffer == id) currentDrawFramebuffer = 0;
                if (currentFramebuffer == id) currentFramebuffer = 0;
                break;
            case GpuResourceKind.VertexArray:
                InvalidateVertexConfiguration(id);
                RecordBoundaryRetirement(EPipelineState.VertexArray, id);
                elementArrayBufferByVao.Remove(id);
                if (currentVao == id) currentVao = 0;
                break;
            case GpuResourceKind.Program:
                RecordBoundaryRetirement(EPipelineState.Program, id);
                break;
            case GpuResourceKind.ProgramPipeline:
                pipelineUniformPrograms.Remove(id);
                RecordBoundaryRetirement(EPipelineState.ProgramPipeline, id);
                if (currentProgramPipeline == id) currentProgramPipeline = 0;
                break;
            case GpuResourceKind.TransformFeedback:
                feedbackActivity.Remove(id);
                RecordBoundaryRetirement(EPipelineState.TransformFeedback, id);
                if (currentTransformFeedback == id) currentTransformFeedback = 0;
                break;
        }
    }
    #endregion

    #region Private
    /// <summary>Checks actual per-object outcomes without consuming the error queue or treating a list as atomic.</summary>
    private static bool WasResourceDeleted(GpuResourceKind kind, nint nativeId)
    {
        int id = unchecked((int)nativeId);
        return kind switch
        {
            GpuResourceKind.Shader => RHI.Backends.OpenGL.OpenGLRenderingDeletionCalls.WasDeletedExternal(kind, nativeId),
            GpuResourceKind.Program => RHI.Backends.OpenGL.OpenGLRenderingDeletionCalls.WasDeletedExternal(kind, nativeId),
            GpuResourceKind.Buffer => RHI.Backends.OpenGL.OpenGLBufferDeletionCalls.WasDeleted(id),
            GpuResourceKind.Texture => RHI.Backends.OpenGL.OpenGLImageDeletionCalls.WasDeleted(kind, id),
            GpuResourceKind.Renderbuffer => RHI.Backends.OpenGL.OpenGLImageDeletionCalls.WasDeleted(kind, id),
            GpuResourceKind.Framebuffer => RHI.Backends.OpenGL.OpenGLRenderingDeletionCalls.WasDeletedExternal(kind, nativeId),
            GpuResourceKind.VertexArray => RHI.Backends.OpenGL.OpenGLRenderingDeletionCalls.WasDeletedExternal(kind, nativeId),
            GpuResourceKind.Sampler => RHI.Backends.OpenGL.OpenGLImageDeletionCalls.WasDeleted(kind, id),
            GpuResourceKind.Query => RHI.Backends.OpenGL.OpenGLAuxiliaryDeletionCalls.WasDeleted(kind, nativeId),
            GpuResourceKind.Sync => RHI.Backends.OpenGL.OpenGLAuxiliaryDeletionCalls.WasDeleted(kind, nativeId),
            GpuResourceKind.ProgramPipeline => RHI.Backends.OpenGL.OpenGLRenderingDeletionCalls.WasDeletedExternal(kind, nativeId),
            GpuResourceKind.TransformFeedback => RHI.Backends.OpenGL.OpenGLAuxiliaryDeletionCalls.WasDeleted(kind, nativeId),
            GpuResourceKind.Semaphore => RHI.Backends.OpenGL.OpenGLAuxiliaryDeletionCalls.WasDeleted(kind, nativeId),
            GpuResourceKind.MemoryObject => RHI.Backends.OpenGL.OpenGLAuxiliaryDeletionCalls.WasDeleted(kind, nativeId),
            _ => false
        };
    }
    #endregion
}
