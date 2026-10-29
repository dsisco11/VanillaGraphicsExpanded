using VanillaGraphicsExpanded.Rendering.RHI;
using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Supplies original source facts to immutable texture-view creation.</summary>
internal sealed partial class RHIResourceManager
{
    #region Internal API
    /// <summary>Creates a separate view object while preserving the original source allocation relationship.</summary>
    internal static GpuTextureMetadata? CreateTextureView(in GpuTextureViewInitialization initialization,
        GpuResource<GpuTextureMetadata> recipient, out GpuResourceHandle handle, GpuResource<GpuTextureMetadata>? source = null)
        => CreateResource<GpuTextureViewInitialization, GpuTextureMetadata, TextureViewCreation>(
            initialization, recipient, out handle, new(source));
    #endregion

    /// <summary>Retains optional managed source provenance only for this synchronous creation operation.</summary>
    private readonly struct TextureViewCreation(GpuResource<GpuTextureMetadata>? source) : IGpuResourceCreation<GpuTextureViewInitialization, GpuTextureMetadata>
    {
        #region Public API
        /// <inheritdoc />
        public GpuResourceKind Kind => GpuResourceKind.Texture;
        /// <inheritdoc />
        public void Validate(in GpuTextureViewInitialization initialization) => initialization.Validate();
        /// <inheritdoc />
        public GpuTextureMetadata? Initialize(RHI.RHICommandContext execution, in GpuTextureViewInitialization initialization, out nint id)
        {
            id = 0;
            RHICommandContext.RequireResourceOperationAvailable(GpuResourceKind.Texture, initialization.Source);
            GpuTextureMetadata backing;
            if (source is not null)
            {
                if (!source.IsValid || (int)source.ResourceId != initialization.Source)
                    throw new InvalidOperationException("Texture view source identity changed.");
                backing = RequireTextureReferenceMetadata(source);
            }
            else if (RHIResourceManagerSystem.CurrentManager is { } manager && manager.CanObserveNativeMetadata)
                backing = manager.AcquireResource(GpuResourceKind.Texture, initialization.Source, GpuTextureMetadataReader.Instance)?.Metadata
                    ?? throw new InvalidOperationException("Texture view source metadata is unavailable.");
            else
                throw new InvalidOperationException("Texture view source metadata is unavailable.");
            return execution.InitializeTextureView(initialization, backing, out id);
        }
        #endregion
    }
}
