namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Orchestrates Semaphore creation through the common private identity and metadata handoff.</summary>
internal sealed partial class GpuResourceManager
{
    #region Internal API
    /// <summary>Requests Semaphore creation; configuration and use remain separate operations.</summary>
    internal static GpuSemaphoreMetadata? CreateSemaphore(in GpuSemaphoreInitialization initialization,
        GpuResource<GpuSemaphoreMetadata> recipient, out GpuResourceHandle handle)
        => CreateResource<GpuSemaphoreInitialization, GpuSemaphoreMetadata, SemaphoreCreation>(initialization, recipient, out handle);
    #endregion

    #region Private
    /// <summary>Adapts the empty request to the shared owned-creation contract.</summary>
    private readonly struct SemaphoreCreation : IGpuResourceCreation<GpuSemaphoreInitialization, GpuSemaphoreMetadata>
    {
        #region Public API
        /// <inheritdoc />
        public GpuResourceKind Kind => GpuResourceKind.Semaphore;
        /// <inheritdoc />
        public void Validate(in GpuSemaphoreInitialization initialization) { }
        /// <inheritdoc />
        public GpuSemaphoreMetadata? Initialize(RHI.RHICommandContext execution, in GpuSemaphoreInitialization initialization, out nint id)
            => execution.InitializeSemaphore(out id);
        #endregion
    }
    #endregion
}
