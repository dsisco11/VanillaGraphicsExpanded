namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns renderbuffer creation and recipient handoff.</summary>
internal sealed partial class RHIResourceManager
{
    #region Internal API
    /// <summary>Creates and publishes complete renderbuffer storage before exposing its handle.</summary>
    internal static GpuRenderbufferMetadata? CreateRenderbuffer(in GpuRenderbufferInitialization initialization,
        GpuResource<GpuRenderbufferMetadata> recipient, out GpuResourceHandle handle)
        => CreateResource<GpuRenderbufferInitialization, GpuRenderbufferMetadata, RenderbufferCreation>(initialization, recipient, out handle);
    #endregion

    /// <summary>Adapts renderbuffer initialization to the common atomic handoff contract.</summary>
    private readonly struct RenderbufferCreation : IGpuResourceCreation<GpuRenderbufferInitialization, GpuRenderbufferMetadata>
    {
        #region Public API
        /// <inheritdoc />
        public GpuResourceKind Kind => GpuResourceKind.Renderbuffer;
        /// <inheritdoc />
        public void Validate(in GpuRenderbufferInitialization initialization) => initialization.Validate();
        /// <inheritdoc />
        public GpuRenderbufferMetadata? Initialize(RHI.RHICommandContext execution, in GpuRenderbufferInitialization initialization, out nint id)
            => execution.InitializeRenderbuffer(initialization, out id);
        #endregion
    }
}
