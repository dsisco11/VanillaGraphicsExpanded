using System;
using System.Collections.Immutable;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Immutable graphics features and limits owned by GpuSupport and consumed by validation.</summary>
public sealed record GraphicsCapabilities
{
    #region Graphics features and limits
    /// <summary>Maximum individually enabled shader clip distances, queried from GL_MAX_CLIP_DISTANCES.</summary>
    public int MaxClipDistances { get; init; }
    /// <summary>Whether core 4.5 or ARB_clip_control supplies alternate clip origin/depth conventions.</summary>
    public bool ClipControl { get; init; }

    /// <summary>Whether the API version meets the OpenGL 3.3 graphics baseline.</summary>
    public bool Graphics33 { get; init; }
    /// <summary>Whether blend equations and factors can be configured independently per draw buffer.</summary>
    public bool IndependentBlend { get; init; }
    /// <summary>Whether minimum per-sample shading can be requested.</summary>
    public bool SampleShading { get; init; }
    /// <summary>Whether tessellation shader stages and patch primitives are supported.</summary>
    public bool Tessellation { get; init; }
    /// <summary>Whether core 4.0 or ARB_transform_feedback2 exposes transform-feedback activity queries.</summary>
    public bool TransformFeedbackActivityQueries { get; init; }
    /// <summary>Whether primitive restart can use the fixed maximum index value for the index type.</summary>
    public bool FixedIndexRestart { get; init; }
    /// <summary>Whether vertex attributes can supply double-precision values.</summary>
    public bool DoubleAttributes { get; init; }
    /// <summary>Whether depth clamping can replace clipping at the near and far planes.</summary>
    public bool DepthClamp { get; init; }
    /// <summary>Whether the context profile mask identifies a core-profile context.</summary>
    public bool CoreProfile { get; init; }
    /// <summary>Maximum number of simultaneous draw-buffer outputs.</summary>
    public int MaxDrawBuffers { get; init; }
    /// <summary>Number of available generic vertex attribute locations.</summary>
    public int MaxVertexAttributes { get; init; }
    /// <summary>Number of vertex-buffer binding slots; falls back to the attribute count without separate attribute bindings.</summary>
    public int MaxVertexBindings { get; init; }
    /// <summary>Maximum vertex binding stride in bytes; int.MaxValue when the stride limit query is unavailable.</summary>
    public int MaxVertexStride { get; init; }
    /// <summary>Maximum attribute offset relative to a vertex-buffer binding, in bytes; int.MaxValue without separate attribute bindings.</summary>
    public int MaxVertexRelativeOffset { get; init; }
    /// <summary>Implementation-wide maximum multisample count; individual formats and targets may impose lower limits.</summary>
    public int MaxSamples { get; init; }
    /// <summary>Number of supported 32-bit sample-mask words.</summary>
    public int MaxSampleMaskWords { get; init; }
    /// <summary>Maximum number of input vertices per tessellation patch, or zero without tessellation support.</summary>
    public int MaxPatchVertices { get; init; }
    /// <summary>Minimum supported aliased line width in pixels.</summary>
    public float MinLineWidth { get; init; }
    /// <summary>Maximum supported aliased line width in pixels.</summary>
    public float MaxLineWidth { get; init; }
    /// <summary>Minimum supported aliased point size in pixels.</summary>
    public float MinPointSize { get; init; }
    /// <summary>Maximum supported aliased point size in pixels.</summary>
    public float MaxPointSize { get; init; }

    #endregion

    #region Context characteristics
    /// <summary>Driver-provided OpenGL version string.</summary>
    public string VersionString { get; init; } = string.Empty;
    /// <summary>Driver-provided implementation vendor name.</summary>
    public string VendorString { get; init; } = string.Empty;
    /// <summary>Driver-provided renderer name.</summary>
    public string RendererString { get; init; } = string.Empty;
    /// <summary>Driver-provided shading-language version string.</summary>
    public string ShadingLanguageVersionString { get; init; } = string.Empty;
    /// <summary>Parsed OpenGL API version, or null when the version string cannot be parsed.</summary>
    public Version? ApiVersion { get; init; }
    /// <summary>Parsed shading-language version, or null when the version string cannot be parsed.</summary>
    public Version? ShadingLanguageVersion { get; init; }
    /// <summary>Whether the version or renderer string identifies an OpenGL ES implementation.</summary>
    public bool IsOpenGles { get; init; }
    /// <summary>Whether the context shares objects with another context; null when sharing cannot be determined.</summary>
    public bool? IsSharedContext { get; init; }
    /// <summary>Raw context flag bit mask reported by OpenGL.</summary>
    public int ContextFlags { get; init; }
    /// <summary>Raw OpenGL context profile bit mask, or zero when the profile query is unavailable.</summary>
    public int ContextProfileMaskValue { get; init; }
    /// <summary>Whether the context flags include the debug bit.</summary>
    public bool IsDebugContext { get; init; }
    /// <summary>Whether the context flags include the forward-compatible bit.</summary>
    public bool IsForwardCompatibleContext { get; init; }
    /// <summary>Whether the context flags include the robust-access bit.</summary>
    public bool IsRobustAccessContext { get; init; }
    /// <summary>Whether the context profile mask identifies a compatibility-profile context.</summary>
    public bool IsCompatibilityProfile { get; init; }
    #endregion

    #region Resource limits
    /// <summary>Maximum width or height of a two-dimensional texture, in texels.</summary>
    public int MaxTextureSize { get; init; }
    /// <summary>Maximum size of each dimension of a three-dimensional texture, in texels.</summary>
    public int Max3DTextureSize { get; init; }
    /// <summary>Maximum width or height of a cube-map face, in texels.</summary>
    public int MaxCubeMapTextureSize { get; init; }
    /// <summary>Maximum number of layers in an array texture.</summary>
    public int MaxArrayTextureLayers { get; init; }
    /// <summary>Maximum number of texture image units available to the fragment shader stage.</summary>
    public int MaxTextureImageUnits { get; init; }
    /// <summary>Maximum combined texture image units across shader stages.</summary>
    public int MaxCombinedTextureImageUnits { get; init; }
    /// <summary>Maximum number of texture image units available to the vertex shader stage.</summary>
    public int MaxVertexTextureImageUnits { get; init; }
    /// <summary>Number of uniform-buffer binding points.</summary>
    public int MaxUniformBufferBindings { get; init; }
    /// <summary>Maximum size of a uniform block, in bytes.</summary>
    public int MaxUniformBlockSize { get; init; }
    /// <summary>Number of shader-storage-buffer binding points.</summary>
    public int MaxShaderStorageBufferBindings { get; init; }
    /// <summary>Maximum size of a shader-storage block, in bytes.</summary>
    public long MaxShaderStorageBlockSize { get; init; }
    /// <summary>Number of atomic-counter-buffer binding points.</summary>
    public int MaxAtomicCounterBufferBindings { get; init; }
    /// <summary>Number of image-unit binding points, distinct from sampler texture units.</summary>
    /// <summary>Limits active image uniforms in one compute executable.</summary>
    public int MaxComputeImageUniforms { get; init; }
    public int MaxImageUnits { get; init; }
    /// <summary>Maximum combined image uniforms across shader stages.</summary>
    public int MaxCombinedImageUnits { get; init; }
    /// <summary>Maximum number of color attachment points on a framebuffer.</summary>
    public int MaxColorAttachments { get; init; }
    /// <summary>Maximum viewport width in pixels.</summary>
    public int MaxViewportWidth { get; init; }
    /// <summary>Maximum viewport height in pixels.</summary>
    public int MaxViewportHeight { get; init; }
    /// <summary>Required alignment of shader-storage-buffer range offsets, in bytes.</summary>
    public int ShaderStorageBufferOffsetAlignment { get; init; }
    /// <summary>Maximum tessellation generation level.</summary>
    public int MaxTessGenLevel { get; init; }
    /// <summary>Maximum texture image units available to the tessellation control shader stage.</summary>
    public int MaxTessControlTextureImageUnits { get; init; }
    /// <summary>Maximum texture image units available to the tessellation evaluation shader stage.</summary>
    public int MaxTessEvaluationTextureImageUnits { get; init; }
    /// <summary>Maximum number of uniform locations available to a program.</summary>
    public int MaxUniformLocations { get; init; }
    /// <summary>Maximum dispatched work-group counts in X, Y and Z order; empty when compute support is unavailable.</summary>
    public ImmutableArray<int> MaxComputeWorkGroupCount { get; init; } = ImmutableArray<int>.Empty;
    /// <summary>Maximum local work-group dimensions in X, Y and Z order; empty when compute support is unavailable.</summary>
    public ImmutableArray<int> MaxComputeWorkGroupSize { get; init; } = ImmutableArray<int>.Empty;
    /// <summary>Maximum total shader invocations in one compute work group.</summary>
    public int MaxComputeWorkGroupInvocations { get; init; }
    /// <summary>Maximum shared memory available to one compute work group, in bytes.</summary>
    public int MaxComputeSharedMemorySize { get; init; }
    #endregion

    #region Extension support
    /// <summary>Whether debug messages and object labels are available through GL_KHR_debug or OpenGL 4.3.</summary>
    public bool SupportsKhrDebug { get; init; }
    /// <summary>Whether direct state access is available through GL_ARB_direct_state_access or OpenGL 4.5.</summary>
    public bool SupportsArbDirectStateAccess { get; init; }
    /// <summary>Whether multiple resources can be bound in one call through GL_ARB_multi_bind or OpenGL 4.4.</summary>
    public bool SupportsArbMultiBind { get; init; }
    /// <summary>Whether GL_ARB_bindless_texture is advertised for texture and image handles.</summary>
    public bool SupportsArbBindlessTexture { get; init; }
    /// <summary>Whether compute shaders are available through GL_ARB_compute_shader or the core API version.</summary>
    public bool SupportsArbComputeShader { get; init; }
    /// <summary>Whether shader-storage buffers are available through GL_ARB_shader_storage_buffer_object or the core API version.</summary>
    public bool SupportsArbShaderStorageBufferObject { get; init; }
    /// <summary>Whether shader image load/store is available through GL_ARB_shader_image_load_store or the core API version.</summary>
    public bool SupportsArbShaderImageLoadStore { get; init; }
    /// <summary>Whether shader atomic counters are available through GL_ARB_shader_atomic_counters or the core API version.</summary>
    public bool SupportsArbShaderAtomicCounters { get; init; }
    /// <summary>Whether explicit uniform locations are available through GL_ARB_explicit_uniform_location or the core API version.</summary>
    public bool SupportsArbExplicitUniformLocation { get; init; }
    /// <summary>Whether immutable buffer storage is available through GL_ARB_buffer_storage or the core API version.</summary>
    public bool SupportsArbBufferStorage { get; init; }
    /// <summary>Whether GLSL 4.20 layout features are available through GL_ARB_shading_language_420pack or the core API version.</summary>
    public bool SupportsArbShadingLanguage420Pack { get; init; }
    /// <summary>Whether program resource queries are available through GL_ARB_program_interface_query or the core API version.</summary>
    public bool SupportsArbProgramInterfaceQuery { get; init; }
    /// <summary>Whether SPIR-V shader ingestion is available through GL_ARB_gl_spirv or the core API version.</summary>
    public bool SupportsArbGlSpirv { get; init; }
    /// <summary>Whether GL_EXT_semaphore is advertised for external semaphore synchronization.</summary>
    public bool SupportsExtSemaphore { get; init; }
    /// <summary>Whether GL_EXT_semaphore_fd is advertised for importing semaphore file descriptors.</summary>
    public bool SupportsExtSemaphoreFd { get; init; }
    /// <summary>Whether GL_EXT_memory_object is advertised for external memory objects.</summary>
    public bool SupportsExtMemoryObject { get; init; }
    /// <summary>Whether GL_EXT_memory_object_fd is advertised for importing memory file descriptors.</summary>
    public bool SupportsExtMemoryObjectFd { get; init; }
    #endregion
    #region Additional resource capabilities
    /// <summary>Required uniform-buffer range offset alignment in bytes.</summary>
    public int UniformBufferOffsetAlignment { get; init; }
    /// <summary>Required texture-buffer range offset alignment in bytes, or zero without range support.</summary>
    public int TextureBufferOffsetAlignment { get; init; }
    /// <summary>Maximum number of texels addressable by a buffer texture.</summary>
    public int MaxTextureBufferSize { get; init; }
    /// <summary>Maximum object-label length including its null terminator, or zero without debug labeling.</summary>
    public int MaxLabelLength { get; init; }
    /// <summary>Driver-supported executable program binary formats.</summary>
    public ImmutableArray<int> ProgramBinaryFormats { get; init; } = ImmutableArray<int>.Empty;
    /// <summary>Whether texture buffer range is available.</summary>
    public bool SupportsTextureBufferRange { get; init; }
    /// <summary>Whether clear texture is available.</summary>
    public bool SupportsClearTexture { get; init; }
    /// <summary>Whether sparse texture is available.</summary>
    public bool SupportsSparseTexture { get; init; }
    /// <summary>Whether sparse buffer is available.</summary>
    public bool SupportsSparseBuffer { get; init; }
    /// <summary>Whether arb parallel shader compile is available.</summary>
    public bool SupportsArbParallelShaderCompile { get; init; }
    /// <summary>Whether khr parallel shader compile is available.</summary>
    public bool SupportsKhrParallelShaderCompile { get; init; }
    /// <summary>Whether internal format query2 is available.</summary>
    public bool SupportsInternalFormatQuery2 { get; init; }
    #endregion
}
