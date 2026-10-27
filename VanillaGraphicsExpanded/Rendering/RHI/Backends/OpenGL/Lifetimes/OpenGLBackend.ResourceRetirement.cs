using System;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Executes kind-specific destruction for an already admitted original identity.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public void RetireResource(GpuResourceHandle resource)
    {
        RequireContext();
        // The queue has certified the original association; each family retains its deletion outcome policy.
        switch (resource.Kind)
        {
            case GpuResourceKind.Buffer:
                DeleteBuffer((int)resource.Id);
                break;
            case GpuResourceKind.VertexArray:
                DeleteVertexArray((int)resource.Id);
                break;
            case GpuResourceKind.Texture:
                DeleteTexture((int)resource.Id);
                break;
            case GpuResourceKind.Framebuffer:
                DeleteFramebuffer((int)resource.Id);
                break;
            case GpuResourceKind.Renderbuffer:
                DeleteRenderbuffer((int)resource.Id);
                break;
            case GpuResourceKind.Query:
                DeleteQuery((int)resource.Id);
                break;
            case GpuResourceKind.Program:
                DeleteProgram((int)resource.Id);
                break;
            case GpuResourceKind.TransformFeedback:
                DeleteTransformFeedback((int)resource.Id);
                break;
            case GpuResourceKind.Sampler:
                DeleteSampler((int)resource.Id);
                break;
            case GpuResourceKind.ProgramPipeline:
                DeleteProgramPipeline((int)resource.Id);
                break;
            case GpuResourceKind.Shader:
                DeleteShader((int)resource.Id);
                break;
            case GpuResourceKind.Sync:
                DeleteSync((IntPtr)resource.Id);
                break;
            case GpuResourceKind.Semaphore:
                DeleteSemaphore((int)resource.Id);
                break;
            case GpuResourceKind.MemoryObject:
                DeleteMemoryObject((int)resource.Id);
                break;
        }
    }
    #endregion
}
