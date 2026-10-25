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
        public void Validate(in GpuSemaphoreInitialization initialization)
        {
            if (!GpuSupport.Graphics.SupportsExtSemaphore)
                throw new System.NotSupportedException("GL_EXT_semaphore is not supported by the current context.");
        }
        /// <inheritdoc />
        public GpuSemaphoreMetadata? Initialize(StateCache cache, in GpuSemaphoreInitialization initialization, out nint id)
            => cache.CreateSemaphore(out id);
        #endregion
    }
    #endregion
}
