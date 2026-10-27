using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Acquires backing facts once and owns buffer-texture initialization.</summary>
internal sealed partial class GpuResourceManager
{
    #region Internal API
    /// <summary>Validates backing range and commits the new texture without transferring backing ownership.</summary>
    internal static GpuTextureMetadata? CreateBufferTexture(in GpuBufferTextureInitialization initialization,
        GpuResource<GpuTextureMetadata> recipient, out GpuResourceHandle handle, GpuResource<GpuBufferMetadata>? source = null)
        => CreateResource<GpuBufferTextureInitialization, GpuTextureMetadata, BufferTextureCreation>(initialization, recipient, out handle, new(source));

    /// <summary>Reuses canonical backing capacity, reading only previously unknown external buffers.</summary>
    internal static GpuBufferMetadata AcquireBufferTextureInput(int buffer)
    {
        StateCache.Current.RequireResourceOperationAvailable(GpuResourceKind.Buffer, buffer);
        if (GpuResourceManagerSystem.CurrentManager is { } manager && manager.CanObserveNativeMetadata)
            return manager.AcquireResource(GpuResourceKind.Buffer, buffer, new GpuBufferMetadataReader())?.Metadata
                ?? throw new InvalidOperationException("Buffer texture backing is unavailable.");
        if (!new GpuBufferMetadataReader().TryRead(buffer, out var metadata))
            throw new InvalidOperationException("Buffer texture backing is unavailable.");
        return metadata;
    }
    #endregion

    /// <summary>Supplies family validation and execution to the common manager handoff.</summary>
    private readonly struct BufferTextureCreation(GpuResource<GpuBufferMetadata>? source) : IGpuResourceCreation<GpuBufferTextureInitialization, GpuTextureMetadata>
    {
        #region Public API
        /// <inheritdoc />
        public GpuResourceKind Kind => GpuResourceKind.Texture;
        /// <inheritdoc />
        public void Validate(in GpuBufferTextureInitialization initialization) => initialization.Validate();
        /// <inheritdoc />
        public GpuTextureMetadata? Initialize(RHI.RHICommandContext execution, in GpuBufferTextureInitialization initialization, out nint id)
        {
            id = 0;
            var backing = source is not null ? RequireBufferReferenceMetadata(source) : AcquireBufferTextureInput(initialization.Buffer);
            if (source is not null && source.ResourceId != initialization.Buffer) throw new InvalidOperationException("The backing buffer identity changed.");
            if (initialization.Mode == GpuBufferTextureMode.Range)
            {
                int alignment = execution.Capabilities.TextureBufferOffsetAlignment;
                if (initialization.Offset > backing.Capacity || initialization.Size > backing.Capacity - initialization.Offset
                    || alignment <= 0 || initialization.Offset % alignment != 0)
                    throw new ArgumentException("Buffer texture range exceeds backing capacity or violates alignment.");
            }
            return execution.InitializeBufferTexture(initialization, out id);
        }
        #endregion
    }
}
