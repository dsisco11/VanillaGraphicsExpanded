using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Supplies original source facts to immutable texture-view creation.</summary>
internal sealed partial class GpuResourceManager
{
    #region Internal API
    /// <summary>Creates a separate view object while preserving the original source allocation relationship.</summary>
    internal static GpuTextureMetadata? CreateTextureView(in GpuTextureViewInitialization initialization,
        GpuResource<GpuTextureMetadata> recipient, out GpuResourceHandle handle, GpuTexture? source = null)
        => CreateResource<GpuTextureViewInitialization, GpuTextureMetadata, TextureViewCreation>(
            initialization, recipient, out handle, new(source));
    #endregion

    /// <summary>Retains optional managed source provenance only for this synchronous creation operation.</summary>
    private readonly struct TextureViewCreation(GpuTexture? source) : IGpuResourceCreation<GpuTextureViewInitialization, GpuTextureMetadata>
    {
        #region Public API
        /// <inheritdoc />
        public GpuResourceKind Kind => GpuResourceKind.Texture;
        /// <inheritdoc />
        public void Validate(in GpuTextureViewInitialization initialization) => initialization.Validate();
        /// <inheritdoc />
        public GpuTextureMetadata? Initialize(StateCache cache, in GpuTextureViewInitialization initialization, out nint id)
        {
            id = 0;
            cache.RequireResourceOperationAvailable(GpuResourceKind.Texture, initialization.Source);
            GpuTextureMetadata backing;
            if (source is not null)
            {
                if (!source.IsValid || source.TextureId != initialization.Source)
                    throw new InvalidOperationException("Texture view source identity changed.");
                backing = AcquireTextureCommandInput(source);
            }
            else if (GpuResourceManagerSystem.CurrentManager is { } manager && manager.CanObserveNativeMetadata)
                backing = manager.AcquireResource(GpuResourceKind.Texture, initialization.Source, GpuTextureMetadataReader.Instance)?.Metadata
                    ?? throw new InvalidOperationException("Texture view source metadata is unavailable.");
            else if (!GpuTextureMetadataReader.Instance.TryRead(initialization.Source, out backing))
                throw new InvalidOperationException("Texture view source metadata is unavailable.");
            return cache.CreateTextureView(initialization, backing, out id);
        }
        #endregion
    }
}
