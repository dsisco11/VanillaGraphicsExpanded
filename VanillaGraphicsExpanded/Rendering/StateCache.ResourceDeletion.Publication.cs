using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Reconciles successful native name deletion with canonical identity and resource-specific binding effects.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Preserves the existing framebuffer configuration guard before a native deletion command.</summary>
    internal void PrepareResourceDeletion(GpuResourceKind kind, int id)
    {
        if (kind == GpuResourceKind.Framebuffer) RequireMutableFramebufferConfiguration(id);
    }

    /// <summary>Retires only proven deleted names and preserves native deferred-destruction behavior.</summary>
    internal void CompleteResourceDeletion(GpuResourceKind kind, nint nativeId)
    {
        if (nativeId == 0 || !WasResourceDeleted(kind, nativeId)) return;
        GpuResourceManagerSystem.CurrentManager?.ObserveDeletion(kind, nativeId);
        int id = unchecked((int)nativeId);
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
                RecordBoundaryRetirement(EPipelineState.VertexArray, id);
                elementArrayBufferByVao.Remove(id);
                if (currentVao == id) currentVao = 0;
                break;
            case GpuResourceKind.Program:
                RecordBoundaryRetirement(EPipelineState.Program, id);
                ProgramScopeTracker.MarkDeleted(id);
                break;
            case GpuResourceKind.ProgramPipeline:
                RecordBoundaryRetirement(EPipelineState.ProgramPipeline, id);
                if (currentProgramPipeline == id) currentProgramPipeline = 0;
                break;
            case GpuResourceKind.TransformFeedback:
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
        // Programs and shaders remain native objects while referenced, even after successful
        // deletion. Their delete-status flag proves retirement independently of physical storage.
        if (kind == GpuResourceKind.Program)
        {
            if (!GL.IsProgram(id)) return true;
            GL.GetProgram(id, GetProgramParameterName.DeleteStatus, out int status);
            return status != 0;
        }
        if (kind == GpuResourceKind.Shader)
        {
            if (!GL.IsShader(id)) return true;
            GL.GetShader(id, ShaderParameter.DeleteStatus, out int status);
            return status != 0;
        }
        return kind switch
        {
            GpuResourceKind.Buffer => !GL.IsBuffer(id),
            GpuResourceKind.Texture => !GL.IsTexture(id),
            GpuResourceKind.Renderbuffer => !GL.IsRenderbuffer(id),
            GpuResourceKind.Framebuffer => !GL.IsFramebuffer(id),
            GpuResourceKind.VertexArray => !GL.IsVertexArray(id),
            GpuResourceKind.Sampler => !GL.IsSampler(id),
            GpuResourceKind.Query => !GL.IsQuery(id),
            GpuResourceKind.Sync => !GL.IsSync((IntPtr)nativeId),
            GpuResourceKind.ProgramPipeline => !GL.IsProgramPipeline(id),
            GpuResourceKind.TransformFeedback => !GL.IsTransformFeedback(id),
            GpuResourceKind.Semaphore => !GL.Ext.IsSemaphore(id),
            GpuResourceKind.MemoryObject => !GL.Ext.IsMemoryObject(id),
            _ => false
        };
    }
    #endregion
}
