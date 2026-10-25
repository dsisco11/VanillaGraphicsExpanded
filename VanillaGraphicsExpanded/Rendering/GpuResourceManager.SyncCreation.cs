namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Orchestrates Sync creation through the common private identity and metadata handoff.</summary>
internal sealed partial class GpuResourceManager
{
    #region Internal API
    /// <summary>Requests Sync creation; configuration and use remain separate operations.</summary>
    internal static GpuSyncMetadata? CreateSync(in GpuSyncInitialization initialization,
        GpuResource<GpuSyncMetadata> recipient, out GpuResourceHandle handle)
        => CreateResource<GpuSyncInitialization, GpuSyncMetadata, SyncCreation>(initialization, recipient, out handle);
    #endregion

    #region Private
    /// <summary>Adapts the empty request to the shared owned-creation contract.</summary>
    private readonly struct SyncCreation : IGpuResourceCreation<GpuSyncInitialization, GpuSyncMetadata>
    {
        #region Public API
        /// <inheritdoc />
        public GpuResourceKind Kind => GpuResourceKind.Sync;
        /// <inheritdoc />
        public void Validate(in GpuSyncInitialization initialization) { }
        /// <inheritdoc />
        public GpuSyncMetadata? Initialize(StateCache cache, in GpuSyncInitialization initialization, out nint id)
            => cache.CreateSync(out id);
        #endregion
    }
    #endregion
}
