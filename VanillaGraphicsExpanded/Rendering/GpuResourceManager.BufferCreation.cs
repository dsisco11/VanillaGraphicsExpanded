namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Creates owned buffers from explicit initialization intent and delivers canonical storage facts.</summary>
internal sealed partial class GpuResourceManager
{
    #region Internal API
    /// <summary>Commits an initialized buffer and its recipient together, exposing identity only on success.</summary>
    internal static GpuBufferMetadata? CreateBuffer(in GpuBufferInitialization initialization,
        GpuResource<GpuBufferMetadata> recipient, out GpuResourceHandle handle)
        => CreateResource<GpuBufferInitialization, GpuBufferMetadata, BufferCreation>(initialization, recipient, out handle);
    #endregion

    /// <summary>Supplies buffer validation and native execution to the common creation handoff.</summary>
    private readonly struct BufferCreation : IGpuResourceCreation<GpuBufferInitialization, GpuBufferMetadata>
    {
        #region Public API
        /// <inheritdoc />
        public GpuResourceKind Kind => GpuResourceKind.Buffer;
        /// <inheritdoc />
        public void Validate(in GpuBufferInitialization initialization)
        {
            initialization.Validate();
            if (initialization.Mode == GpuBufferStorageMode.Immutable && !GpuSupport.Graphics.SupportsArbBufferStorage)
                throw new System.NotSupportedException("Immutable buffer storage is unavailable.");
        }
        /// <inheritdoc />
        public GpuBufferMetadata? Initialize(StateCache cache, in GpuBufferInitialization initialization, out nint id)
            => cache.CreateBuffer(initialization, out id);
        #endregion
    }
}
