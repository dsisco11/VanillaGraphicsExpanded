namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Creates owned buffers from explicit initialization intent and delivers canonical storage facts.</summary>
internal sealed partial class RHIResourceManager
{
    #region Internal API
    /// <summary>Commits an initialized buffer and its recipient together, exposing identity only on success.</summary>
    internal static GpuBufferMetadata? CreateBuffer(in GpuBufferInitialization initialization,
        GpuResource<GpuBufferMetadata> recipient, out GpuResourceHandle handle)
        => CreateBuffer(initialization, recipient, default, out handle);

    /// <summary>Keeps client content stable throughout allocation and private publication handoff.</summary>
    internal static unsafe GpuBufferMetadata? CreateBuffer(in GpuBufferInitialization initialization,
        GpuResource<GpuBufferMetadata> recipient, RHI.RHIBufferInitialData initialData, out GpuResourceHandle handle)
    {
        fixed (byte* bytes = initialData.Bytes)
            return CreateResource<GpuBufferInitialization, GpuBufferMetadata, BufferCreation>(initialization,
                recipient, out handle, new BufferCreation((nint)bytes, initialData.Bytes.Length, initialData.Offset));
    }
    #endregion

    /// <summary>Supplies buffer validation and native execution to the common creation handoff.</summary>
    private readonly struct BufferCreation(nint data, int byteCount, nint offset) : IGpuResourceCreation<GpuBufferInitialization, GpuBufferMetadata>
    {
        #region Public API
        /// <inheritdoc />
        public GpuResourceKind Kind => GpuResourceKind.Buffer;
        /// <inheritdoc />
        public void Validate(in GpuBufferInitialization initialization)
        {
            initialization.Validate();
        }
        /// <inheritdoc />
        public GpuBufferMetadata? Initialize(RHI.RHICommandContext execution, in GpuBufferInitialization initialization, out nint id)
            => execution.InitializeBuffer(initialization, out id, data, byteCount, offset);
        #endregion
    }
}
