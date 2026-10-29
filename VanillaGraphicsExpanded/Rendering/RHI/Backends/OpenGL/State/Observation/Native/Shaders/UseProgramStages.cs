using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Maintains empty pipeline identities and invalidates only execution-owned stage selection.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Realizes a generated pipeline and records a relevant stage configuration revision.</summary>
    [InterceptOpenGL("glUseProgramStages")]
    internal static void ObserveUseProgramStages(uint pipeline, int stages, uint program)
    {
        const int valid = (int)(ProgramStageMask.VertexShaderBit | ProgramStageMask.FragmentShaderBit
            | ProgramStageMask.GeometryShaderBit | ProgramStageMask.TessControlShaderBit
            | ProgramStageMask.TessEvaluationShaderBit | ProgramStageMask.ComputeShaderBit);
        if (pipeline == 0 || stages != (int)ProgramStageMask.AllShaderBits && (stages & ~valid) != 0
            || !OpenGLObservationContext.TryGet(out var cache, out _, false)) return;
        var manager = GpuResourceManagerSystem.CurrentManager!;
        var entry = manager.FindObservedResource(GpuResourceKind.ProgramPipeline, unchecked((int)pipeline))
            as GpuResourceMetadataEntry<GpuProgramPipelineMetadata>;
        if (entry is null) return;
        bool reserved = !entry.IsRealized;
        if (!manager.RealizeObservedProgramPipeline(unchecked((int)pipeline))) return;
        if (reserved) cache.RecordPipelineCreated(unchecked((int)pipeline));
        if (stages == 0) return;
        var feedback = OpenGLFeedbackBindings.GetExternalActivity(cache);
        if (cache.currentProgramPipeline == unchecked((int)pipeline) && feedback.Active && !feedback.Paused) return;
        if (program != 0 && manager.FindObservedResource(GpuResourceKind.Program, unchecked((int)program))
            is GpuResourceMetadataEntry<GpuProgramMetadata> { IsUsable: true, Metadata.ExecutableAvailable: false }) return;
        // Stage configuration belongs to this pipeline. A monolithic UseProgram selection
        // remains fully known, and no canonical stage-layout mirror is introduced.
        cache.InterruptNativeResourceOperation(GpuResourceKind.ProgramPipeline, unchecked((int)pipeline));
        if (InterruptNativeMetadataCompletion(entry, GpuMetadataChange.Metadata)) return;
        manager.PublishMetadata(entry, new GpuProgramPipelineMetadata(), GpuMetadataChange.Metadata);
    }

    /// <summary>Validation also realizes generated names without adding a validation-result descriptor.</summary>
    [InterceptOpenGL("glValidateProgramPipeline")]
    internal static void ObserveValidateProgramPipeline(uint pipeline)
    {
        if (pipeline == 0 || !OpenGLObservationContext.TryGet(out var cache, out _, false)) return;
        var manager = GpuResourceManagerSystem.CurrentManager!;
        if (manager.FindObservedResource(GpuResourceKind.ProgramPipeline, unchecked((int)pipeline)) is { IsRealized: false }
            && manager.RealizeObservedProgramPipeline(unchecked((int)pipeline))) cache.RecordPipelineCreated(unchecked((int)pipeline));
    }

    /// <summary>Consumes explicit pipeline getters' realization and active-program output without issuing another query.</summary>
    [InterceptOpenGL("glGetProgramPipelineiv")]
    internal static unsafe void ObserveGetProgramPipeline(uint pipeline, int parameter, int* result)
    {
        if (pipeline == 0 || result == null || !OpenGLObservationContext.TryGet(out var cache, out _, false)) return;
        var manager = GpuResourceManagerSystem.CurrentManager!;
        int id = unchecked((int)pipeline);
        if (manager.FindObservedResource(GpuResourceKind.ProgramPipeline, id) is not { } entry) return;
        if (!entry.IsRealized && manager.RealizeObservedProgramPipeline(id)) cache.RecordPipelineCreated(id);
        if (parameter == (int)ProgramPipelineParameter.ActiveProgram && entry.IsRealized)
            cache.pipelineUniformPrograms[id] = *result;
    }
    #endregion
}
