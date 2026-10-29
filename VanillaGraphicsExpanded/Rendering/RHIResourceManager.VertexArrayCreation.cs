namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Orchestrates VertexArray creation through the common private identity and metadata handoff.</summary>
internal sealed partial class RHIResourceManager
{
    #region Internal API
    /// <summary>Requests VertexArray creation; configuration and use remain separate operations.</summary>
    internal static GpuVertexArrayMetadata? CreateVertexArray(in GpuVertexArrayInitialization initialization,
        GpuResource<GpuVertexArrayMetadata> recipient, out GpuResourceHandle handle)
        => CreateResource<GpuVertexArrayInitialization, GpuVertexArrayMetadata, VertexArrayCreation>(initialization, recipient, out handle);
    #endregion

    #region Private
    /// <summary>Adapts the empty request to the shared owned-creation contract.</summary>
    private readonly struct VertexArrayCreation : IGpuResourceCreation<GpuVertexArrayInitialization, GpuVertexArrayMetadata>
    {
        #region Public API
        /// <inheritdoc />
        public GpuResourceKind Kind => GpuResourceKind.VertexArray;
        /// <inheritdoc />
        public void Validate(in GpuVertexArrayInitialization initialization) { }
        /// <inheritdoc />
        public GpuVertexArrayMetadata? Initialize(RHI.RHICommandContext execution, in GpuVertexArrayInitialization initialization, out nint id)
            => execution.CreateVertexArray(out id);
        #endregion
    }
    #endregion
}
