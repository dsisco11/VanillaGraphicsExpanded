using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Separates executable stage selection, uniform destination and pipeline-object selection.</summary>
internal sealed partial class OpenGlStateCache
{
    private readonly Dictionary<int, int> pipelineUniformPrograms = [];

    #region Internal API
    /// <summary>Retains guaranteed initial uniform selection for a newly created pipeline object.</summary>
    internal void RecordPipelineCreated(int pipeline)
    {
        if (pipeline != 0) pipelineUniformPrograms[pipeline] = 0;
    }

    /// <summary>Uses observed active-program history before acquiring an initially unknown pipeline selector.</summary>
    internal int GetPipelineUniformProgram(int pipeline)
    {
        if (pipelineUniformPrograms.TryGetValue(pipeline, out int known)) return known;
        var origin = Integration.RenderContextRegistry.CaptureResourceContext();
        GL.GetProgramPipeline(pipeline, ProgramPipelineParameter.ActiveProgram, out int program);
        if (!Integration.RenderContextRegistry.IsCurrentResourceContext(origin))
            throw new System.InvalidOperationException("Pipeline discovery returned in a foreign context.");
        pipelineUniformPrograms[pipeline] = program;
        return program;
    }

    /// <summary>Reconciles the completed native transition using local acceptance and original-context facts.</summary>
    internal static void ObserveProgramSelection(NativeProgramSelector selector,
        int value, int pipeline = 0, int stage = 0)
    {
        if (!OpenGLObservationContext.TryGet(out var cache, out _, false)) return;
        int? previous = selector == NativeProgramSelector.Pipeline ? cache.currentProgramPipeline
            : selector == NativeProgramSelector.PipelineUniform
                ? cache.pipelineUniformPrograms.TryGetValue(pipeline, out int active) ? active : null
                : cache.currentProgram;
        bool stagesKnown = cache.currentProgramStagesKnown;
        bool accepted = false, rejected = false;
        bool linked = value == 0;
        var manager = GpuResourceManagerSystem.CurrentManager;
        if (selector == NativeProgramSelector.Pipeline && value != 0
            && manager?.FindObservedResource(GpuResourceKind.ProgramPipeline, value) is { IsRealized: false }
            && manager.RealizeObservedProgramPipeline(value)) cache.RecordPipelineCreated(value);
        if (selector == NativeProgramSelector.PipelineUniform && pipeline != 0
            && manager?.FindObservedResource(GpuResourceKind.ProgramPipeline, pipeline) is { IsRealized: false }
            && manager.RealizeObservedProgramPipeline(pipeline))
        { cache.RecordPipelineCreated(pipeline); previous = 0; }
        if (selector != NativeProgramSelector.Pipeline && value != 0 && manager?.CanObserveNativeMetadata == true
            && manager.FindObservedResource(GpuResourceKind.Program, value) is GpuResourceMetadataEntry<GpuProgramMetadata> { IsUsable: true } program)
        { linked = program.Metadata.ExecutableAvailable; rejected = !linked; }
        if (selector == NativeProgramSelector.Pipeline)
            accepted = value == 0 || cache.HasEstablishedNativeIdentity(GpuResourceKind.ProgramPipeline, value)
                || cache.pipelineUniformPrograms.ContainsKey(value);
        else if (selector == NativeProgramSelector.PipelineUniform)
        {
            rejected |= pipeline == 0;
            accepted = pipeline != 0 && linked && (cache.HasEstablishedNativeIdentity(GpuResourceKind.ProgramPipeline, pipeline)
                || cache.pipelineUniformPrograms.ContainsKey(pipeline));
        }
        else if (selector == NativeProgramSelector.Stage)
        {
            bool validStage = stage is (int)ShaderType.VertexShader or (int)ShaderType.GeometryShader or (int)ShaderType.FragmentShader;
            rejected |= !validStage;
            // Stage executables remain intentionally unrepresented. Keep the independent
            // uniform destination, and prevent full-program suppression after a stage change.
            accepted = linked && validStage;
        }
        else accepted = linked;
        if (selector is NativeProgramSelector.Program or NativeProgramSelector.Pipeline or NativeProgramSelector.Stage)
        {
            var activity = OpenGLFeedbackBindings.GetExternalActivity(cache);
            if (activity.Active && !activity.Paused) { rejected = true; accepted = false; }
        }
        var context = Integration.RenderContextRegistry.CaptureResourceContext();

        if (!cache.OwnsKnowledgeFor(context) || !Integration.RenderContextRegistry.IsCurrentResourceContext(context)) return;
        int? result = rejected ? previous : accepted ? value : null;
        if (selector == NativeProgramSelector.Pipeline) cache.currentProgramPipeline = result;
        else if (selector == NativeProgramSelector.PipelineUniform)
        {
            if (result.HasValue) cache.pipelineUniformPrograms[pipeline] = result.Value;
            else cache.pipelineUniformPrograms.Remove(pipeline);
        }
        else if (selector == NativeProgramSelector.Stage)
        { cache.currentProgramStagesKnown = rejected && stagesKnown; }
        else
        {
            cache.currentProgram = result;
            cache.currentProgramStagesKnown = rejected ? stagesKnown
                : accepted && (selector == NativeProgramSelector.Program || previous == value && stagesKnown);
        }
    }
    #endregion

    /// <summary>Identifies which native selector changes without conflating independent stage state.</summary>
    internal enum NativeProgramSelector { Program, Pipeline, ContextUniform, PipelineUniform, Stage }

}
