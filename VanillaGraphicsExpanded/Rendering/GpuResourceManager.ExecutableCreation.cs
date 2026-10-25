namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Creates shader and program identities through the common private handoff boundary.</summary>
internal sealed partial class GpuResourceManager
{
    #region Internal API
    /// <summary>Creates a fixed-stage shader without rediscovering its stage.</summary>
    internal static GpuShaderMetadata? CreateShader(in GpuShaderInitialization initialization,
        GpuResource<GpuShaderMetadata> recipient, out GpuResourceHandle handle)
        => CreateResource<GpuShaderInitialization, GpuShaderMetadata, ShaderCreation>(initialization, recipient, out handle);

    /// <summary>Creates an empty unlinked program without reflecting an executable.</summary>
    internal static GpuProgramMetadata? CreateProgram(in GpuProgramInitialization initialization,
        GpuResource<GpuProgramMetadata> recipient, out GpuResourceHandle handle)
        => CreateResource<GpuProgramInitialization, GpuProgramMetadata, ProgramCreation>(initialization, recipient, out handle);
    #endregion

    /// <summary>Adapts shader initialization to atomic resource handoff.</summary>
    private readonly struct ShaderCreation : IGpuResourceCreation<GpuShaderInitialization, GpuShaderMetadata>
    {
        #region Public API
        /// <inheritdoc />
        public GpuResourceKind Kind => GpuResourceKind.Shader;
        /// <inheritdoc />
        public void Validate(in GpuShaderInitialization initialization) => initialization.Validate();
        /// <inheritdoc />
        public GpuShaderMetadata? Initialize(StateCache cache, in GpuShaderInitialization initialization, out nint id)
            => cache.CreateShader(initialization, out id);
        #endregion
    }

    /// <summary>Adapts empty program creation to atomic resource handoff.</summary>
    private readonly struct ProgramCreation : IGpuResourceCreation<GpuProgramInitialization, GpuProgramMetadata>
    {
        #region Public API
        /// <inheritdoc />
        public GpuResourceKind Kind => GpuResourceKind.Program;
        /// <inheritdoc />
        public void Validate(in GpuProgramInitialization initialization) { }
        /// <inheritdoc />
        public GpuProgramMetadata? Initialize(StateCache cache, in GpuProgramInitialization initialization, out nint id)
            => cache.CreateProgram(out id);
        #endregion
    }
}
