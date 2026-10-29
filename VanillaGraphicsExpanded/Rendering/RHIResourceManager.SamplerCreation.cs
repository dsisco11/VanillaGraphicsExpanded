namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Orchestrates Sampler creation through the common private identity and metadata handoff.</summary>
internal sealed partial class RHIResourceManager
{
    #region Internal API
    /// <summary>Requests Sampler creation; configuration and use remain separate operations.</summary>
    internal static GpuSamplerMetadata? CreateSampler(in GpuSamplerInitialization initialization,
        GpuResource<GpuSamplerMetadata> recipient, out GpuResourceHandle handle)
        => CreateResource<GpuSamplerInitialization, GpuSamplerMetadata, SamplerCreation>(initialization, recipient, out handle);
    #endregion

    #region Private
    /// <summary>Adapts the empty request to the shared owned-creation contract.</summary>
    private readonly struct SamplerCreation : IGpuResourceCreation<GpuSamplerInitialization, GpuSamplerMetadata>
    {
        #region Public API
        /// <inheritdoc />
        public GpuResourceKind Kind => GpuResourceKind.Sampler;
        /// <inheritdoc />
        public void Validate(in GpuSamplerInitialization initialization) { }
        /// <inheritdoc />
        public GpuSamplerMetadata? Initialize(RHI.RHICommandContext execution, in GpuSamplerInitialization initialization, out nint id)
            => execution.InitializeSampler(out id);
        #endregion
    }
    #endregion
}
