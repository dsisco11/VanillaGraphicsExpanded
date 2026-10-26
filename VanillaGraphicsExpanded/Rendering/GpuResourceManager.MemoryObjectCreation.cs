namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Orchestrates MemoryObject creation through the common private identity and metadata handoff.</summary>
internal sealed partial class GpuResourceManager
{
    #region Internal API
    /// <summary>Requests MemoryObject creation; configuration and use remain separate operations.</summary>
    internal static GpuMemoryObjectMetadata? CreateMemoryObject(in GpuMemoryObjectInitialization initialization,
        GpuResource<GpuMemoryObjectMetadata> recipient, out GpuResourceHandle handle)
        => CreateResource<GpuMemoryObjectInitialization, GpuMemoryObjectMetadata, MemoryObjectCreation>(initialization, recipient, out handle);
    #endregion

    #region Private
    /// <summary>Adapts the empty request to the shared owned-creation contract.</summary>
    private readonly struct MemoryObjectCreation : IGpuResourceCreation<GpuMemoryObjectInitialization, GpuMemoryObjectMetadata>
    {
        #region Public API
        /// <inheritdoc />
        public GpuResourceKind Kind => GpuResourceKind.MemoryObject;
        /// <inheritdoc />
        public void Validate(in GpuMemoryObjectInitialization initialization) { }
        /// <inheritdoc />
        public GpuMemoryObjectMetadata? Initialize(StateCache cache, in GpuMemoryObjectInitialization initialization, out nint id)
            => RHI.RHICommandContext.Current.OpenGL.InitializeMemoryObject(out id);
        #endregion
    }
    #endregion
}
