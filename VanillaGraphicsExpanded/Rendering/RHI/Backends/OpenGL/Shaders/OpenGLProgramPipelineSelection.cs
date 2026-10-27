using System;
using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Executes native pipeline-object selection at existing explicit integration and restoration boundaries.</summary>
internal static class OpenGLProgramPipelineSelection
{
    #region Internal API
    /// <summary>Publishes only a completed originating-context selection, leaving arbitrary external calls on their exact observation path.</summary>
    internal static void Bind(OpenGlStateCache cache,int pipeline)
    {
        if(cache.ProgramPipelineSelectionKnowledge==pipeline)return;
        var origin=GpuResource.CaptureCreationContext(GpuResourceKind.ProgramPipeline);
        cache.SetProgramPipelineSelectionKnowledge(null);
        GL.BindProgramPipeline(pipeline);
        if(!GpuResource.IsCurrentResourceContext(origin))throw new InvalidOperationException("Pipeline selection returned in a foreign context.");
        cache.SetProgramPipelineSelectionKnowledge(pipeline);
    }
    #endregion
}
