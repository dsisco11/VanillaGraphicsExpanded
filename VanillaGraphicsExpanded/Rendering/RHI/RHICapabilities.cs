namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Projects semantic limits from the existing immutable capability snapshot.</summary>
public sealed class RHICapabilities
{
    private readonly GraphicsCapabilities source;

    #region Public API
    /// <summary>Gets the cached MaxRenderbufferSize allocation limit without new discovery.</summary>
    public int MaxRenderbufferSize => source.MaxRenderbufferSize;
    /// <summary>Gets the cached MaxFramebufferWidth allocation limit without new discovery.</summary>
    public int MaxFramebufferWidth => source.MaxFramebufferWidth;
    /// <summary>Gets the cached MaxFramebufferHeight allocation limit without new discovery.</summary>
    public int MaxFramebufferHeight => source.MaxFramebufferHeight;
    /// <summary>Gets the cached MaxFramebufferLayers allocation limit without new discovery.</summary>
    public int MaxFramebufferLayers => source.MaxFramebufferLayers;
    /// <summary>Gets the cached MaxFramebufferSamples allocation limit without new discovery.</summary>
    public int MaxFramebufferSamples => source.MaxFramebufferSamples;
    /// <summary>Gets the existing cached TextureBufferOffsetAlignment capability without native discovery.</summary>
    public int TextureBufferOffsetAlignment => source.TextureBufferOffsetAlignment;
    /// <summary>Gets the existing cached MaxCubeMapTextureSize capability without native discovery.</summary>
    public int MaxCubeMapTextureSize => source.MaxCubeMapTextureSize;
    /// <summary>Gets the existing cached MaxRectangleTextureSize capability without native discovery.</summary>
    public int MaxRectangleTextureSize => source.MaxRectangleTextureSize;
    /// <summary>Gets the existing cached Max3DTextureSize capability without native discovery.</summary>
    public int Max3DTextureSize => source.Max3DTextureSize;
    /// <summary>Gets the existing cached MaxTextureSize capability without native discovery.</summary>
    public int MaxTextureSize => source.MaxTextureSize;
    /// <summary>Gets the existing cached MaxArrayTextureLayers capability without native discovery.</summary>
    public int MaxArrayTextureLayers => source.MaxArrayTextureLayers;
    /// <summary>Gets the existing cached MaxImageUnits capability without native discovery.</summary>
    public int MaxImageUnits => source.MaxImageUnits;
    /// <summary>Gets the existing cached MaxCombinedTextureImageUnits capability without native discovery.</summary>
    public int MaxCombinedTextureImageUnits => source.MaxCombinedTextureImageUnits;
    /// <summary>Gets the existing cached SupportsOvrMultiview capability without native discovery.</summary>
    public bool Multiview => source.SupportsOvrMultiview;
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
    /// <summary>Reports optional sparse-image residency support.</summary>
    public bool SparseTextures => source.SupportsSparseTexture;
    /// <summary>Gets the cached sparse-buffer commitment page size.</summary>
    public int SparseBufferPageSize => source.SparseBufferPageSize;
    /// <summary>Reports support for optional shader SPIR-V ingestion.</summary>
    public bool SpirvShaders => source.SupportsArbGlSpirv;
    /// <summary>Reports support for optional deferred shader compilation.</summary>
    public bool ParallelShaderCompilation => source.SupportsArbParallelShaderCompile || source.SupportsKhrParallelShaderCompile;
    /// <summary>Reports native indirect-count support from the cached API version or extension inventory.</summary>
    public bool IndirectDrawCount { get; }
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
    /// <summary>Reports optional Windows memory import support from cached extension discovery.</summary>
    public bool ExternalMemoryWin32 { get; }
    /// <summary>Reports optional pipeline-statistics query support from cached extensions.</summary>
    public bool PipelineStatistics { get; }
    /// <summary>Reports optional Windows semaphore import support from cached extension discovery.</summary>
    public bool ExternalSemaphoresWin32 { get; }
    #endregion

    #region Internal API
    /// <summary>Retains existing discovery results rather than querying a second capability model.</summary>
    internal RHICapabilities(GraphicsCapabilities source)
    {
        this.source = source;
        IndirectDrawCount = source.ApiVersion is { } version && version >= new System.Version(4, 6)
            || GpuSupport.Supports("GL_ARB_indirect_parameters");
        PipelineStatistics = GpuSupport.Supports("GL_ARB_pipeline_statistics_query");
        ExternalMemoryWin32 = GpuSupport.Supports("GL_EXT_memory_object_win32");
        ExternalSemaphoresWin32 = GpuSupport.Supports("GL_EXT_semaphore_win32");
        TextureAnisotropy = GpuSupport.Supports("GL_EXT_texture_filter_anisotropic") || GpuSupport.Supports("GL_ARB_texture_filter_anisotropic");
        CoverageSampling = GpuSupport.Supports("GL_NV_framebuffer_multisample_coverage") && GpuSupport.Supports("GL_EXT_direct_state_access");
        S3tcTextures = GpuSupport.Supports("GL_EXT_texture_compression_s3tc");
        Dxt1Textures = S3tcTextures || GpuSupport.Supports("GL_EXT_texture_compression_dxt1");
    }
    #endregion
}
