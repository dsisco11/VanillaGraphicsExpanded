using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Deletes admitted original rendering objects before shared lifecycle and selector reconciliation.</summary>
internal sealed partial class OpenGLBackend
{
    #region Internal API
    /// <summary>Withdraws a selected owned executable before deletion so later scopes cannot restore its freed name.</summary>
    internal void DeleteProgram(int id)
    {
        RequireContext();
        // A deleted current program stays natively bound until selection changes. Managed retirement
        // ends our selection first; exact external deletion retains OpenGL's deferred binding semantics.
        if (id != 0 && cache.GetCurrentProgram() == id)
        {
            OpenGLDriverCalls.UseProgram(0);
            RequireContext();
            cache.NotifyProgramBound(0);
        }
        OpenGLDriverCalls.DeleteProgram(id); RequireContext();
        cache.CompleteResourceDeletion(GpuResourceKind.Program, id, OpenGLRenderingDeletionCalls.WasDeletedManaged(GpuResourceKind.Program, id));
    }
    /// <summary>Marks a stage deleted while preserving native references held by attached programs.</summary>
    internal void DeleteShader(int id)
    {
        RequireContext(); OpenGLDriverCalls.DeleteShader(id); RequireContext();
        cache.CompleteResourceDeletion(GpuResourceKind.Shader, id, OpenGLRenderingDeletionCalls.WasDeletedManaged(GpuResourceKind.Shader, id));
    }
    /// <summary>Retires an original program-pipeline name through the common lifetime service.</summary>
    internal void DeleteProgramPipeline(int id)
    {
        RequireContext(); OpenGLDriverCalls.DeleteProgramPipeline(id); RequireContext();
        cache.CompleteResourceDeletion(GpuResourceKind.ProgramPipeline, id, OpenGLRenderingDeletionCalls.WasDeletedManaged(GpuResourceKind.ProgramPipeline, id));
    }
    /// <summary>Checks existing configuration guards before native framebuffer deletion.</summary>
    internal void DeleteFramebuffer(int id)
    {
        RequireContext(); cache.RequireMutableFramebufferConfiguration(id);
        OpenGLDriverCalls.DeleteFramebuffer(id); RequireContext();
        cache.CompleteResourceDeletion(GpuResourceKind.Framebuffer, id, OpenGLRenderingDeletionCalls.WasDeletedManaged(GpuResourceKind.Framebuffer, id));
    }
    /// <summary>Retires a VAO and repairs only the affected existing binding facts.</summary>
    internal void DeleteVertexArray(int id)
    {
        RequireContext(); OpenGLDriverCalls.DeleteVertexArray(id); RequireContext();
        cache.CompleteResourceDeletion(GpuResourceKind.VertexArray, id, OpenGLRenderingDeletionCalls.WasDeletedManaged(GpuResourceKind.VertexArray, id));
    }
    #endregion
}
