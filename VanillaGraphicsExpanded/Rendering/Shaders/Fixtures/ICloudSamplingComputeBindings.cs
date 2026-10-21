using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Binds independently authored schedule queries, the shared immutable points and bounded results.</summary>
internal interface ICloudSamplingComputeBindings
{
    #region Public API
    /// <summary>Supplies two unsigned vectors per schedule query.</summary>
    [ShaderBinding("SamplingQueries",ShaderBindingKind.StorageBlock,0,ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Queries {set;}
    /// <summary>Returns observation index, offset and selection for each query.</summary>
    [ShaderBinding("SamplingResults",ShaderBindingKind.StorageBlock,1,ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Results {set;}
    /// <summary>Uses the same quantized sequence resource as the live cloud draw.</summary>
    [ShaderBinding("pmjJitter",ShaderBindingKind.Sampler,0,ShaderStageKind.Compute,TextureTarget=ShaderTextureTarget.Texture2D,Sampler=ShaderSamplerPolicy.Default)]
    GpuTexture? PmjJitter {set;}
    #endregion
}
