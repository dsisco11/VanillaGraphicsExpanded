namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns framebuffer initialization and private recipient handoff.</summary>
internal sealed partial class GpuResourceManager
{
    #region Internal API
    /// <summary>Creates a framebuffer from immutable selections and resolved defaults.</summary>
    internal static GpuFramebufferMetadata? CreateFramebuffer(in GpuFramebufferInitialization initialization,
        GpuResource<GpuFramebufferMetadata> recipient, out GpuResourceHandle handle)
        => CreateResource<GpuFramebufferInitialization, GpuFramebufferMetadata, FramebufferCreation>(initialization, recipient, out handle);
    #endregion

    /// <summary>Adapts framebuffer commands to the shared handoff boundary.</summary>
    private readonly struct FramebufferCreation : IGpuResourceCreation<GpuFramebufferInitialization, GpuFramebufferMetadata>
    {
        #region Public API
        /// <inheritdoc />
        public GpuResourceKind Kind => GpuResourceKind.Framebuffer;
        /// <inheritdoc />
        public void Validate(in GpuFramebufferInitialization initialization) => initialization.Validate();
        /// <inheritdoc />
        public GpuFramebufferMetadata? Initialize(StateCache cache, in GpuFramebufferInitialization initialization, out nint id)
            => cache.CreateFramebuffer(initialization, out id);
        #endregion
    }
}
