namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Orchestrates TransformFeedback creation through the common private identity and metadata handoff.</summary>
internal sealed partial class RHIResourceManager
{
    #region Internal API
    /// <summary>Requests TransformFeedback creation; configuration and use remain separate operations.</summary>
    internal static GpuTransformFeedbackMetadata? CreateTransformFeedback(in GpuTransformFeedbackInitialization initialization,
        GpuResource<GpuTransformFeedbackMetadata> recipient, out GpuResourceHandle handle)
        => CreateResource<GpuTransformFeedbackInitialization, GpuTransformFeedbackMetadata, TransformFeedbackCreation>(initialization, recipient, out handle);
    #endregion

    #region Private
    /// <summary>Adapts the empty request to the shared owned-creation contract.</summary>
    private readonly struct TransformFeedbackCreation : IGpuResourceCreation<GpuTransformFeedbackInitialization, GpuTransformFeedbackMetadata>
    {
        #region Public API
        /// <inheritdoc />
        public GpuResourceKind Kind => GpuResourceKind.TransformFeedback;
        /// <inheritdoc />
        public void Validate(in GpuTransformFeedbackInitialization initialization) { }
        /// <inheritdoc />
        public GpuTransformFeedbackMetadata? Initialize(RHI.RHICommandContext execution, in GpuTransformFeedbackInitialization initialization, out nint id)
            => execution.InitializeTransformFeedback(out id);
        #endregion
    }
    #endregion
}
