namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Creates owned texture storage from resolved inputs through the common recipient handoff.</summary>
internal sealed partial class GpuResourceManager
{
    #region Internal API
    /// <summary>Allocates a complete texture privately and publishes its established image descriptors.</summary>
    internal static GpuTextureMetadata? CreateTexture(in GpuTextureInitialization initialization,
        GpuResource<GpuTextureMetadata> recipient, out GpuResourceHandle handle, RHI.RHITextureInitialData[]? initialData = null)
    {
        var result = CreateResource<GpuTextureInitialization, GpuTextureMetadata, TextureCreation>(initialization, recipient, out handle, new(initialData));
        // Local tools have no registry revision. A newly created allocation must replace
        // their storage certificate even when the driver reuses the previous numeric name.
        if (result.HasValue && recipient is GpuTexture texture && recipient.ResourceManager?.GetResourceRegistration(recipient) is null)
            texture.UntrackedStorageIdentity = new();
        return result;
    }
    #endregion

    /// <summary>Adapts texture input validation and allocation to manager-owned publication.</summary>
    private readonly struct TextureCreation(RHI.RHITextureInitialData[]? initialData) : IGpuResourceCreation<GpuTextureInitialization, GpuTextureMetadata>
    {
        #region Public API
        /// <inheritdoc />
        public GpuResourceKind Kind => GpuResourceKind.Texture;
        /// <inheritdoc />
        public void Validate(in GpuTextureInitialization initialization) => _ = initialization.Resolve();
        /// <inheritdoc />
        public GpuTextureMetadata? Initialize(RHI.RHICommandContext execution, in GpuTextureInitialization initialization, out nint id)
            => execution.InitializeTexture(initialization.Resolve(), out id, initialData);
        #endregion
    }
}
