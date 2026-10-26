namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Projects semantic limits from the existing immutable capability snapshot.</summary>
public sealed class RHICapabilities
{
    private readonly GraphicsCapabilities source;

    #region Public API
    /// <summary>Gets the number of vertex input locations.</summary>
    public int MaxVertexAttributes => source.MaxVertexAttributes;
    /// <summary>Gets the available draw-output count.</summary>
    public int MaxColorOutputs => source.MaxDrawBuffers;
    /// <summary>Gets the implementation-wide sample bound; formats retain their cached specific limits.</summary>
    public int MaxSamples => source.MaxSamples;
    /// <summary>Gets the number of uniform-buffer slots.</summary>
    public int MaxUniformBufferBindings => source.MaxUniformBufferBindings;
    /// <summary>Gets the maximum uniform block range in bytes.</summary>
    public int MaxUniformBlockSize => source.MaxUniformBlockSize;
    /// <summary>Gets the number of shader-storage slots.</summary>
    public int MaxShaderStorageBufferBindings => source.MaxShaderStorageBufferBindings;
    /// <summary>Gets the number of atomic-counter slots.</summary>
    public int MaxAtomicCounterBufferBindings => source.MaxAtomicCounterBufferBindings;
    /// <summary>Gets the number of transform-feedback slots.</summary>
    public int MaxTransformFeedbackBuffers => source.MaxTransformFeedbackBuffers;
    /// <summary>Gets the required uniform range offset alignment.</summary>
    public int UniformBufferOffsetAlignment => source.UniformBufferOffsetAlignment;
    /// <summary>Gets the required shader-storage range offset alignment.</summary>
    public int ShaderStorageBufferOffsetAlignment => source.ShaderStorageBufferOffsetAlignment;
    /// <summary>Reports support for optional sparse buffer residency.</summary>
    public bool SparseBuffers => source.SupportsSparseBuffer;
    /// <summary>Reports support for optional shader SPIR-V ingestion.</summary>
    public bool SpirvShaders => source.SupportsArbGlSpirv;
    /// <summary>Reports support for optional deferred shader compilation.</summary>
    public bool ParallelShaderCompilation => source.SupportsArbParallelShaderCompile || source.SupportsKhrParallelShaderCompile;
    /// <summary>Reports support for optional external semaphore operations.</summary>
    public bool ExternalSemaphores => source.SupportsExtSemaphore;
    /// <summary>Reports support for optional external memory objects.</summary>
    public bool ExternalMemory => source.SupportsExtMemoryObject;
    /// <summary>Reports optional anisotropic filtering from the existing cached extension inventory.</summary>
    public bool TextureAnisotropy { get; }
    /// <summary>Reports optional named coverage-sample storage support.</summary>
    public bool CoverageSampling { get; }
    /// <summary>Reports the optional complete S3TC compressed-format family.</summary>
    public bool S3tcTextures { get; }
    /// <summary>Reports DXT1 support, including implementations exposing only that S3TC subset.</summary>
    public bool Dxt1Textures { get; }
    #endregion

    #region Internal API
    /// <summary>Retains existing discovery results rather than querying a second capability model.</summary>
    internal RHICapabilities(GraphicsCapabilities source)
    {
        this.source = source;
        TextureAnisotropy = GpuSupport.Supports("GL_EXT_texture_filter_anisotropic") || GpuSupport.Supports("GL_ARB_texture_filter_anisotropic");
        CoverageSampling = GpuSupport.Supports("GL_NV_framebuffer_multisample_coverage") && GpuSupport.Supports("GL_EXT_direct_state_access");
        S3tcTextures = GpuSupport.Supports("GL_EXT_texture_compression_s3tc");
        Dxt1Textures = S3tcTextures || GpuSupport.Supports("GL_EXT_texture_compression_dxt1");
    }
    #endregion
}
