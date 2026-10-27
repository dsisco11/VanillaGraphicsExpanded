namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Orchestrates ProgramPipeline creation through the common private identity and metadata handoff.</summary>
internal sealed partial class GpuResourceManager
{
    #region Internal API
    /// <summary>Requests ProgramPipeline creation; configuration and use remain separate operations.</summary>
    internal static GpuProgramPipelineMetadata? CreateProgramPipeline(in GpuProgramPipelineInitialization initialization,
        GpuResource<GpuProgramPipelineMetadata> recipient, out GpuResourceHandle handle)
        => CreateResource<GpuProgramPipelineInitialization, GpuProgramPipelineMetadata, ProgramPipelineCreation>(initialization, recipient, out handle);
    #endregion

    #region Private
    /// <summary>Adapts the empty request to the shared owned-creation contract.</summary>
    private readonly struct ProgramPipelineCreation : IGpuResourceCreation<GpuProgramPipelineInitialization, GpuProgramPipelineMetadata>
    {
        #region Public API
        /// <inheritdoc />
        public GpuResourceKind Kind => GpuResourceKind.ProgramPipeline;
        /// <inheritdoc />
        public void Validate(in GpuProgramPipelineInitialization initialization) { }
        /// <inheritdoc />
        public GpuProgramPipelineMetadata? Initialize(StateCache cache, in GpuProgramPipelineInitialization initialization, out nint id)
            => RHI.RHICommandContext.Current.OpenGL.InitializeProgramPipeline(out id);
        #endregion
    }
    #endregion
}
