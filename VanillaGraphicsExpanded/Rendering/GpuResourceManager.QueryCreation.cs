namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Orchestrates Query creation through the common private identity and metadata handoff.</summary>
internal sealed partial class GpuResourceManager
{
    #region Internal API
    /// <summary>Requests Query creation; configuration and use remain separate operations.</summary>
    internal static GpuQueryMetadata? CreateQuery(in GpuQueryInitialization initialization,
        GpuResource<GpuQueryMetadata> recipient, out GpuResourceHandle handle)
        => CreateResource<GpuQueryInitialization, GpuQueryMetadata, QueryCreation>(initialization, recipient, out handle);
    #endregion

    #region Private
    /// <summary>Adapts the empty request to the shared owned-creation contract.</summary>
    private readonly struct QueryCreation : IGpuResourceCreation<GpuQueryInitialization, GpuQueryMetadata>
    {
        #region Public API
        /// <inheritdoc />
        public GpuResourceKind Kind => GpuResourceKind.Query;
        /// <inheritdoc />
        public void Validate(in GpuQueryInitialization initialization) { }
        /// <inheritdoc />
        public GpuQueryMetadata? Initialize(RHI.RHICommandContext execution, in GpuQueryInitialization initialization, out nint id)
            => execution.InitializeQuery(initialization.Kind, out id);
        #endregion
    }
    #endregion
}
